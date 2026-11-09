using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý tin tức hiển thị trên cổng thông tin công khai (chỉ Admin).</summary>
    [Authorize(Roles = "Admin")]
    public class NewsController : Controller
    {
        private readonly AppDbContext _db;
        public NewsController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index(string? keyword, NewsCategory? category, string? status, int page = 1)
        {
            var q = _db.News.AsNoTracking().Include(n => n.Tournament).AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                q = q.Where(n => EF.Functions.Collate(n.Title, AppDbContext.SearchCollation).Contains(kw)
                              || EF.Functions.Collate(n.Summary, AppDbContext.SearchCollation).Contains(kw));
            }
            if (category.HasValue) q = q.Where(n => n.Category == category);
            q = status switch
            {
                "published" => q.Where(n => n.IsPublished),
                "draft" => q.Where(n => !n.IsPublished),
                "featured" => q.Where(n => n.IsFeatured),
                _ => q
            };
            ViewBag.Keyword = keyword;
            ViewBag.Category = category;
            ViewBag.Status = status;
            return View(await PagedList<News>.CreateAsync(q.OrderByDescending(n => n.PublishedAt), page, 15));
        }

        public async Task<IActionResult> Create()
        {
            await LoadTournaments(null);
            return View("Form", new News { AuthorName = User.FindFirst("FullName")?.Value });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var n = await _db.News.FindAsync(id);
            if (n == null) return NotFound();
            await LoadTournaments(n.TournamentId);
            return View("Form", n);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(News input)
        {
            if (!ModelState.IsValid)
            {
                await LoadTournaments(input.TournamentId);
                return View("Form", input);
            }

            News? n;
            if (input.Id == 0)
            {
                n = new News { CreatedAt = DateTime.Now };
                _db.News.Add(n);
            }
            else
            {
                n = await _db.News.FindAsync(input.Id);
                if (n == null) return NotFound();
            }
            n.Title = input.Title.Trim();
            n.Summary = input.Summary.Trim();
            n.Content = input.Content.Trim();
            n.ImageUrl = input.ImageUrl;
            n.Category = input.Category;
            n.TournamentId = input.TournamentId;
            n.IsFeatured = input.IsFeatured;
            n.IsPublished = input.IsPublished;
            n.PublishedAt = input.PublishedAt == default ? DateTime.Now : input.PublishedAt;
            n.AuthorName = input.AuthorName;
            n.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            TempData["Success"] = input.Id == 0 ? "Đã đăng bài viết mới." : "Đã cập nhật bài viết.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id, string field)
        {
            var n = await _db.News.FindAsync(id);
            if (n == null) return NotFound();
            if (field == "featured") n.IsFeatured = !n.IsFeatured;
            else if (field == "published") n.IsPublished = !n.IsPublished;
            n.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã cập nhật trạng thái bài “{n.Title}”.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int id)
        {
            var n = await _db.News.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return n == null ? NotFound() : View(n);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var n = await _db.News.FindAsync(id);
            if (n == null) return NotFound();
            _db.News.Remove(n);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xóa bài viết.";
            return RedirectToAction(nameof(Index));
        }

        private async Task LoadTournaments(int? selected)
        {
            var ts = await _db.Tournaments.OrderByDescending(t => t.Season).ThenBy(t => t.Name).ToListAsync();
            ViewBag.Tournaments = ts.Select(t => new SelectListItem($"{t.Name} {t.Season}", t.Id.ToString(), t.Id == selected)).ToList();
        }
    }
}
