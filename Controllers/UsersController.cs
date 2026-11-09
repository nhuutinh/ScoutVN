using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý tài khoản người dùng (chỉ Admin).</summary>
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IAuthService _auth;

        public UsersController(AppDbContext db, IAuthService auth)
        {
            _db = db;
            _auth = auth;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        public async Task<IActionResult> Index(string? keyword, UserRole? role, string? status)
        {
            var q = _db.Users.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                q = q.Where(u => u.Username.Contains(kw) ||
                                 EF.Functions.Collate(u.FullName, AppDbContext.SearchCollation).Contains(kw) ||
                                 (u.Email != null && u.Email.Contains(kw)));
            }
            if (role.HasValue) q = q.Where(u => u.Role == role);
            if (status == "pending") q = q.Where(u => !u.IsApproved);
            else if (status == "active") q = q.Where(u => u.IsApproved && u.IsActive);
            else if (status == "locked") q = q.Where(u => u.IsApproved && !u.IsActive);
            ViewBag.Keyword = keyword;
            ViewBag.Role = role;
            ViewBag.Status = status;
            ViewBag.Pending = await _db.Users.CountAsync(u => !u.IsApproved);
            // Tài khoản chờ duyệt lên đầu
            return View(await q.OrderBy(u => u.IsApproved).ThenBy(u => u.Role).ThenBy(u => u.Username).ToListAsync());
        }

        /// <summary>Phê duyệt tài khoản tự đăng ký → được phép đăng nhập.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var u = await _db.Users.FindAsync(id);
            if (u == null) return NotFound();
            u.IsApproved = true;
            u.IsActive = true;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã phê duyệt tài khoản {u.Username}. Người dùng đã có thể đăng nhập.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Từ chối yêu cầu đăng ký → xóa tài khoản chưa được duyệt.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var u = await _db.Users.FindAsync(id);
            if (u == null) return NotFound();
            if (u.IsApproved)
            {
                TempData["Error"] = "Chỉ từ chối được tài khoản đang chờ phê duyệt.";
                return RedirectToAction(nameof(Index));
            }
            _db.Users.Remove(u);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã từ chối và xóa yêu cầu đăng ký của {u.Username}.";
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Create() => View("Form", new UserFormViewModel());

        private const string OnlyLockMsg = "Quản trị viên chỉ được khóa / mở khóa tài khoản của người dùng khác, không được sửa thông tin của họ.";

        // Chỉ được sửa thông tin tài khoản của chính mình; tài khoản người khác chỉ khóa / mở khóa
        public async Task<IActionResult> Edit(int id)
        {
            if (id != CurrentUserId)
            {
                TempData["Error"] = OnlyLockMsg;
                return RedirectToAction(nameof(Index));
            }
            var u = await _db.Users.FindAsync(id);
            if (u == null) return NotFound();
            return View("Form", new UserFormViewModel
            {
                Id = u.Id, Username = u.Username, FullName = u.FullName, Email = u.Email, Role = u.Role, IsActive = u.IsActive
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(UserFormViewModel model)
        {
            if (model.Id != 0 && model.Id != CurrentUserId)
            {
                TempData["Error"] = OnlyLockMsg;
                return RedirectToAction(nameof(Index));
            }
            model.Username = UserNames.Clean(model.Username);
            if (string.IsNullOrEmpty(model.Username))
                ModelState.AddModelError(nameof(model.Username), "Vui lòng nhập tên đăng nhập");
            if (await _db.Users.AnyAsync(u => u.Username == model.Username && u.Id != model.Id))
                ModelState.AddModelError(nameof(model.Username), "Tên đăng nhập đã tồn tại");
            if (model.Id == 0 && string.IsNullOrEmpty(model.Password))
                ModelState.AddModelError(nameof(model.Password), "Vui lòng nhập mật khẩu cho tài khoản mới");
            if (model.Id == CurrentUserId && (model.Role != UserRole.Admin || !model.IsActive))
                ModelState.AddModelError(string.Empty, "Bạn không thể tự hạ quyền hoặc khóa tài khoản của chính mình");
            if (!ModelState.IsValid) return View("Form", model);

            AppUser? user;
            if (model.Id == 0)
            {
                user = new AppUser { IsApproved = true };   // tài khoản do Admin tạo không cần duyệt
                _db.Users.Add(user);
            }
            else
            {
                user = await _db.Users.FindAsync(model.Id);
                if (user == null) return NotFound();
            }

            user.Username = model.Username;
            user.FullName = model.FullName.Trim();
            user.Email = model.Email;
            user.Role = model.Role;
            user.IsActive = model.IsActive;
            if (!string.IsNullOrEmpty(model.Password))
                user.PasswordHash = _auth.HashPassword(user, model.Password);

            await _db.SaveChangesAsync();
            TempData["Success"] = model.Id == 0 ? "Đã tạo tài khoản." : "Đã cập nhật tài khoản.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            if (id == CurrentUserId)
            {
                TempData["Error"] = "Bạn không thể khóa tài khoản của chính mình.";
                return RedirectToAction(nameof(Index));
            }
            var u = await _db.Users.FindAsync(id);
            if (u == null) return NotFound();
            u.IsActive = !u.IsActive;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã {(u.IsActive ? "mở khóa" : "khóa")} tài khoản {u.Username}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
