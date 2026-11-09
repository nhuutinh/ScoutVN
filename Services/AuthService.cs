using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;

namespace ScoutVN.Services
{
    public interface IAuthService
    {
        string HashPassword(AppUser user, string password);
        Task<AppUser?> ValidateAsync(string username, string password);
        /// <summary>Kiểm tra đăng nhập; trả về lỗi cụ thể (sai mật khẩu / chờ phê duyệt / bị khóa).</summary>
        Task<(AppUser? User, string? Error)> CheckLoginAsync(string username, string password);
        Task SignInAsync(HttpContext http, AppUser user, bool remember);
        Task SignOutAsync(HttpContext http);
    }

    /// <summary>Xác thực bằng Cookie; mật khẩu băm bằng PasswordHasher (PBKDF2) của ASP.NET Core Identity.</summary>
    /// <summary>Chuẩn hóa tên đăng nhập: bỏ khoảng trắng đầu/cuối, đưa chữ có dấu về một dạng Unicode (NFC)
    /// để "Tình" gõ bằng bộ gõ nào cũng khớp.</summary>
    public static class UserNames
    {
        public static string Clean(string? s) => (s ?? "").Trim().Normalize(System.Text.NormalizationForm.FormC);
    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher<AppUser> _hasher;

        public AuthService(AppDbContext db, IPasswordHasher<AppUser> hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public string HashPassword(AppUser user, string password) => _hasher.HashPassword(user, password);

        public async Task<AppUser?> ValidateAsync(string username, string password)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            var (ok, _) = await CheckLoginAsync(username, password);
            return ok;
        }

        public async Task<(AppUser? User, string? Error)> CheckLoginAsync(string username, string password)
        {
            const string wrong = "Sai tên đăng nhập hoặc mật khẩu.";
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return (null, wrong);
            if (_hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
                return (null, wrong);
            // Chỉ báo trạng thái tài khoản khi đã nhập đúng mật khẩu (tránh lộ thông tin tài khoản)
            if (!user.IsApproved)
                return (null, "Tài khoản của bạn đang chờ quản trị viên phê duyệt. Vui lòng quay lại sau khi được duyệt.");
            if (!user.IsActive)
                return (null, "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");
            return (user, null);
        }

        public async Task SignInAsync(HttpContext http, AppUser user, bool remember)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("FullName", user.FullName),
                new(ClaimTypes.Role, user.Role.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var props = new AuthenticationProperties
            {
                IsPersistent = remember,
                ExpiresUtc = remember ? DateTimeOffset.UtcNow.AddDays(7) : null
            };
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);
        }

        public Task SignOutAsync(HttpContext http)
            => http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
