using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IAuthService _auth;
        private readonly IMemoryCache _mem;
        private const int MaxFails = 5;
        private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);

        public AccountController(AppDbContext db, IAuthService auth, IMemoryCache mem)
        {
            _db = db;
            _auth = auth;
            _mem = mem;
        }

        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Dashboard", "Home");
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Chống dò mật khẩu: một tài khoản nhập sai 5 lần liên tiếp → tạm khóa đăng nhập 5 phút
            var uname = UserNames.Clean(model.Username);
            var failKey = "login-fail:" + uname.ToLowerInvariant();
            if (_mem.TryGetValue(failKey, out int fails) && fails >= MaxFails)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản này đã nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau 5 phút.");
                return View(model);
            }
            var (user, error) = await _auth.CheckLoginAsync(uname, model.Password);
            if (user == null)
            {
                _mem.Set(failKey, fails + 1, LockFor);
                ModelState.AddModelError(string.Empty, error ?? "Không đăng nhập được.");
                return View(model);
            }

            _mem.Remove(failKey);
            await _auth.SignInAsync(HttpContext, user, model.RememberMe);
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);
            return RedirectToAction("Dashboard", "Home");
        }

        [HttpGet, AllowAnonymous]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            model.Username = UserNames.Clean(model.Username);
            if (string.IsNullOrEmpty(model.Username))
                ModelState.AddModelError(nameof(model.Username), "Vui lòng nhập tên đăng nhập");
            else if (await _db.Users.AnyAsync(u => u.Username == model.Username))
                ModelState.AddModelError(nameof(model.Username), "Tên đăng nhập đã tồn tại");
            if (!ModelState.IsValid) return View(model);

            // Tài khoản tự đăng ký luôn là HLV/Tuyển trạch viên; Admin chỉ do Admin cấp
            var user = new AppUser
            {
                Username = model.Username,
                FullName = model.FullName.Trim(),
                Email = model.Email,
                Role = UserRole.Scout,
                IsApproved = false   // phải chờ quản trị viên phê duyệt
            };
            user.PasswordHash = _auth.HashPassword(user, model.Password);
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Đăng ký thành công! Tài khoản của bạn đang chờ quản trị viên phê duyệt – bạn có thể đăng nhập sau khi được duyệt.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _auth.SignOutAsync(HttpContext);
            return RedirectToAction("Home", "Portal");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();
    }
}
