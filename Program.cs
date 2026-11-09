using System.Globalization;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== Thông tin bí mật (chuỗi kết nối CSDL, khóa API) KHÔNG để trong mã nguồn / appsettings.json =====
// Đọc theo thứ tự ưu tiên tăng dần:
//   appsettings.json (để trống) → appsettings.Local.json (máy cá nhân, đã thêm vào .gitignore)
//   → user-secrets (dotnet user-secrets set "ConnectionStrings:DefaultConnection" "...")
//   → biến môi trường ConnectionStrings__DefaultConnection (khi triển khai lên máy chủ).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
if (builder.Environment.IsDevelopment()) builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Chưa cấu hình chuỗi kết nối CSDL. Tạo tệp appsettings.Local.json cạnh appsettings.json với nội dung " +
        "{ \"ConnectionStrings\": { \"DefaultConnection\": \"Server=...;Database=ScoutVN_VLeague;...\" } } " +
        "hoặc đặt biến môi trường ConnectionStrings__DefaultConnection (xem README).");

// ===== CSDL: SQL Server qua Entity Framework Core =====
// - DbContextPool: tái sử dụng đối tượng DbContext giữa các yêu cầu (giảm cấp phát bộ nhớ khi đông người dùng).
// - EnableRetryOnFailure: tự thử lại khi SQL Server bận / mất kết nối thoáng qua (deadlock, timeout ngắn).
builder.Services.AddDbContextPool<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
    {
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
        sql.CommandTimeout(30);
    }));

// ===== Hiệu năng khi nhiều người dùng cùng lúc =====
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDataCache, DataCache>();   // bộ nhớ đệm tra cứu dùng chung (xem Services/DataCache.cs)
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;   // nén HTML/CSS/JS (Brotli/Gzip) → trang tải nhanh hơn, đỡ băng thông
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Giới hạn tần suất để một người không làm nghẽn cả hệ thống:
// - "ai": mỗi tài khoản tối đa 15 câu hỏi / phút cho trợ lý AI (mỗi câu gọi API ngoài, tốn thời gian).
// - "login": mỗi địa chỉ IP tối đa 60 lần đăng nhập / đăng ký mỗi phút (đủ cho cả lớp / cả phòng dùng chung mạng;
//   chống dò mật khẩu theo từng tài khoản đã có ở AccountController: sai 5 lần → khóa tạm 5 phút).
var aiPerMinute = builder.Configuration.GetValue("RateLimit:AiPerMinute", 15);
var loginPerMinute = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 60);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("ai", ctx => RateLimitPartition.GetSlidingWindowLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new SlidingWindowRateLimiterOptions { PermitLimit = aiPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        var http = ctx.HttpContext;
        const string msg = "Bạn thao tác quá nhanh, vui lòng đợi khoảng một phút rồi thử lại.";
        if (http.Request.Path.StartsWithSegments("/Assistant"))
        {
            http.Response.ContentType = "application/json; charset=utf-8";
            await http.Response.WriteAsync("{\"error\":\"" + msg + "\"}", ct);
        }
        else
        {
            http.Response.ContentType = "text/html; charset=utf-8";
            await http.Response.WriteAsync("<!doctype html><meta charset=utf-8><title>Thử lại sau</title><body style=\"font-family:sans-serif;padding:3rem;text-align:center\"><h2>" + msg + "</h2><p><a href=\"javascript:history.back()\">← Quay lại</a></p></body>", ct);
        }
    };
});

// ===== Xác thực bằng Cookie + phân quyền theo vai trò =====
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Tài khoản bị Admin khóa (hoặc chưa duyệt) bị đăng xuất ngay ở lần truy cập kế tiếp,
        // không phải đợi hết hạn cookie. Kết quả kiểm tra lưu đệm & tự làm mới khi bảng tài khoản thay đổi.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var idText = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idText, out var uid)) return;
            var sp = ctx.HttpContext.RequestServices;
            var cache = sp.GetRequiredService<IDataCache>();
            var ok = await cache.GetAsync("user-ok:" + uid, async () =>
            {
                var db = sp.GetRequiredService<AppDbContext>();
                return await db.Users.AnyAsync(u => u.Id == uid && u.IsActive && u.IsApproved);
            }, TimeSpan.FromMinutes(2));
            if (!ok)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<IKnnService, KnnService>();
builder.Services.AddScoped<IStatsQaService, StatsQaService>();
builder.Services.AddScoped<AiDbTools>();
builder.Services.AddScoped<IClubValuationService, ClubValuationService>();

// ===== Trợ lý AI: khóa API chỉ đọc ở máy chủ (appsettings / user-secrets / biến môi trường AiAssistant__ApiKey) =====
builder.Services.Configure<AiAssistantOptions>(builder.Configuration.GetSection("AiAssistant"));
builder.Services.AddHttpClient("llm");
builder.Services.AddScoped<IAiAssistantService, AiAssistantService>();

builder.Services.AddControllersWithViews(o => o.Filters.Add<ScoutVN.Filters.DbConflictFilter>());

// Không mã hóa ký tự tiếng Việt thành &#x...; trong HTML (trang gọn hơn, dễ đọc mã nguồn)
builder.Services.Configure<WebEncoderOptions>(o =>
    o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

var app = builder.Build();

// ===== Tạo CSDL + nhập dữ liệu V.League thật khi chạy lần đầu =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
    await DbSeeder.InitializeAsync(db, hasher);
}

// Định dạng số/ngày theo tiếng Việt (dd/MM/yyyy). Dùng dấu chấm thập phân để form nhập số không bị lỗi.
var vi = new CultureInfo("vi-VN");
vi.NumberFormat.NumberDecimalSeparator = ".";
vi.NumberFormat.NumberGroupSeparator = ",";
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(vi),
    SupportedCultures = new[] { vi },
    SupportedUICultures = new[] { vi }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
// Tệp tĩnh (CSS/JS/ảnh): cho trình duyệt lưu đệm. Tệp có ?v=... (asp-append-version) lưu 30 ngày vì đổi nội dung là đổi v.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var versioned = ctx.Context.Request.Query.ContainsKey("v");
        ctx.Context.Response.Headers.CacheControl = versioned ? "public,max-age=2592000,immutable" : "public,max-age=86400";
    }
});

app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
