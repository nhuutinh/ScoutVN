using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Services;
using ScoutVN.Models.ViewModels;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý đội bóng: xem/tìm kiếm (mọi người dùng), thêm/sửa/xóa (Admin).</summary>
    [Authorize]
    public class TeamsController : Controller
    {
        private readonly AppDbContext _db;

        public TeamsController(AppDbContext db) => _db = db;

        // GET: /Teams?keyword=&city=&sort=&page=
        public async Task<IActionResult> Index(string? keyword, string? city, string? sort, int page = 1)
        {
            var query = _db.Teams.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(t =>
                    EF.Functions.Collate(t.Name, AppDbContext.SearchCollation).Contains(kw) ||
                    (t.ShortName != null && EF.Functions.Collate(t.ShortName, AppDbContext.SearchCollation).Contains(kw)) ||
                    (t.Stadium != null && EF.Functions.Collate(t.Stadium, AppDbContext.SearchCollation).Contains(kw)));
            }
            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(t => t.City == city);

            var items = query.Select(t => new TeamListItem
            {
                Team = t,
                PlayerCount = t.Players.Count,
                AverageOverall = t.Players.Average(p => (double?)p.Overall) ?? 0,
                ForeignCount = t.Players.Count(p => p.Nationality != "Việt Nam")
            });

            items = sort switch
            {
                "name_desc" => items.OrderByDescending(x => x.Team.Name),
                "city" => items.OrderBy(x => x.Team.City).ThenBy(x => x.Team.Name),
                "players" => items.OrderByDescending(x => x.PlayerCount),
                "ovr" => items.OrderByDescending(x => x.AverageOverall),
                _ => items.OrderBy(x => x.Team.Name)
            };

            var vm = new TeamIndexViewModel
            {
                Keyword = keyword,
                City = city,
                Sort = sort,
                Cities = await _db.Teams.Select(t => t.City).Distinct().OrderBy(c => c).ToListAsync(),
                Teams = await PagedList<TeamListItem>.CreateAsync(items, page, 10)
            };
            return View(vm);
        }

        // GET: /Teams/Details/5
        public async Task<IActionResult> Details(int id, [FromServices] IClubValuationService valuation)
        {
            var team = await _db.Teams.AsNoTracking()
                .Include(t => t.Players)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound();

            team.Players = team.Players.OrderBy(p => p.Position).ThenByDescending(p => p.Overall).ToList();
            ViewBag.Value = await valuation.ForTeamAsync(id);
            return View(team);
        }

        // GET: /Teams/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create() => View(new Team());

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Team team)
        {
            await ValidateUniqueName(team);
            if (!ModelState.IsValid) return View(team);

            _db.Teams.Add(team);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã thêm đội bóng \"{team.Name}\".";
            return RedirectToAction(nameof(Details), new { id = team.Id });
        }

        // GET: /Teams/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var team = await _db.Teams.FindAsync(id);
            return team == null ? NotFound() : View(team);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Team input)
        {
            if (id != input.Id) return BadRequest();
            await ValidateUniqueName(input);
            if (!ModelState.IsValid) return View(input);

            var team = await _db.Teams.FindAsync(id);
            if (team == null) return NotFound();

            team.Name = input.Name.Trim();
            team.ShortName = input.ShortName;
            team.City = input.City.Trim();
            team.Stadium = input.Stadium;
            team.FoundedYear = input.FoundedYear;
            team.HeadCoach = input.HeadCoach;
            team.LogoUrl = input.LogoUrl;
            team.Description = input.Description;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Đã cập nhật đội bóng \"{team.Name}\".";
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: /Teams/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var team = await _db.Teams.AsNoTracking().Include(t => t.Players).FirstOrDefaultAsync(t => t.Id == id);
            return team == null ? NotFound() : View(team);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var team = await _db.Teams.Include(t => t.Players).FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound();

            // Đội đã có trận đấu trong hệ thống thì không cho xóa (giữ toàn vẹn kết quả và bảng xếp hạng)
            var matchCount = await _db.Matches.CountAsync(m => m.HomeTeamId == id || m.AwayTeamId == id);
            if (matchCount > 0)
            {
                TempData["Error"] = $"Không thể xóa \"{team.Name}\" vì đội đã có {matchCount} trận đấu trong hệ thống. Hãy xóa các trận đấu liên quan trước.";
                return RedirectToAction(nameof(Details), new { id });
            }
            // Thống kê đã ghi nhận cho đội: giữ lại số liệu, bỏ liên kết đội
            foreach (var st in await _db.PlayerStats.Where(s => s.TeamId == id).ToListAsync()) st.TeamId = null;

            // Cầu thủ của đội chuyển sang trạng thái tự do thay vì bị xóa
            foreach (var p in team.Players)
            {
                p.TeamId = null;
                p.Team = null;
                p.ShirtNumber = null;
                if (p.Status == PlayerStatus.Active) p.Status = PlayerStatus.FreeAgent;
                p.UpdatedAt = DateTime.Now;
            }
            _db.Teams.Remove(team);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Đã xóa đội bóng \"{team.Name}\". {team.Players.Count} cầu thủ đã chuyển sang trạng thái tự do.";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateUniqueName(Team team)
        {
            if (string.IsNullOrWhiteSpace(team.Name)) return;
            var name = team.Name.Trim();
            if (await _db.Teams.AnyAsync(t => t.Name == name && t.Id != team.Id))
                ModelState.AddModelError(nameof(Team.Name), "Tên đội bóng đã tồn tại");
        }
    }
}
