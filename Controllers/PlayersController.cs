using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý cầu thủ: tìm kiếm/lọc nhiều tiêu chí, xem hồ sơ, thêm/sửa/xóa (Admin).</summary>
    [Authorize]
    public class PlayersController : Controller
    {
        private readonly AppDbContext _db;

        public PlayersController(AppDbContext db) => _db = db;

        // GET: /Players?Keyword=&TeamId=&Position=&MinAge=&MaxAge=&Sort=&Page=
        public async Task<IActionResult> Index([FromQuery] PlayerFilter filter)
        {
            var query = _db.Players.AsNoTracking().Include(p => p.Team).AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                var kw = filter.Keyword.Trim();
                query = query.Where(p =>
                    EF.Functions.Collate(p.FullName, AppDbContext.SearchCollation).Contains(kw) ||
                    (p.Hometown != null && EF.Functions.Collate(p.Hometown, AppDbContext.SearchCollation).Contains(kw)));
            }
            if (filter.TeamId.HasValue)
            {
                // TeamId = 0 nghĩa là "chưa có đội"
                query = filter.TeamId == 0
                    ? query.Where(p => p.TeamId == null)
                    : query.Where(p => p.TeamId == filter.TeamId);
            }
            if (filter.Position.HasValue) query = query.Where(p => p.Position == filter.Position);
            if (filter.Foot.HasValue) query = query.Where(p => p.PreferredFoot == filter.Foot);
            if (filter.Status.HasValue) query = query.Where(p => p.Status == filter.Status);
            if (!string.IsNullOrWhiteSpace(filter.Nationality))
            {
                query = filter.Nationality == "__foreign"
                    ? query.Where(p => p.Nationality != "Việt Nam")
                    : query.Where(p => p.Nationality == filter.Nationality);
            }

            // Lọc theo tuổi => đổi sang khoảng ngày sinh để SQL xử lý được
            var today = DateTime.Today;
            if (filter.MinAge.HasValue) query = query.Where(p => p.DateOfBirth <= today.AddYears(-filter.MinAge.Value));
            if (filter.MaxAge.HasValue) query = query.Where(p => p.DateOfBirth > today.AddYears(-(filter.MaxAge.Value + 1)));
            if (filter.MinHeight.HasValue) query = query.Where(p => p.HeightCm >= filter.MinHeight);

            query = filter.Sort switch
            {
                "name" => query.OrderBy(p => p.FullName),
                "name_desc" => query.OrderByDescending(p => p.FullName),
                "age" => query.OrderByDescending(p => p.DateOfBirth),      // trẻ nhất trước
                "age_desc" => query.OrderBy(p => p.DateOfBirth),
                "value" => query.OrderByDescending(p => p.MarketValue),
                "team" => query.OrderBy(p => p.Team != null ? p.Team.Name : "~").ThenBy(p => p.Position),
                "position" => query.OrderBy(p => p.Position).ThenByDescending(p => p.Overall),
                "updated" => query.OrderByDescending(p => p.UpdatedAt),
                _ => query.OrderByDescending(p => p.Overall).ThenBy(p => p.FullName)
            };

            await LoadFilterLists();
            return View(new PlayerIndexViewModel
            {
                Filter = filter,
                Players = await PagedList<Player>.CreateAsync(query, filter.Page, filter.PageSize)
            });
        }

        // GET: /Players/Details/5
        public async Task<IActionResult> Details(int id, [FromServices] ScoutVN.Services.IAnalysisService analysis)
        {
            var player = await _db.Players.AsNoTracking()
                .Include(p => p.Team)
                .Include(p => p.Stats).ThenInclude(s => s.Tournament)
                .Include(p => p.Stats).ThenInclude(s => s.Team)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (player == null) return NotFound();

            player.Stats = player.Stats
                .OrderByDescending(s => s.Tournament!.Season).ThenBy(s => s.Tournament!.Type)
                .ToList();
            ViewBag.Insights = await analysis.InsightsAsync(player);
            return View(player);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int? teamId)
        {
            await LoadFormLists(teamId);
            return View(new Player { TeamId = teamId });
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Player player)
        {
            await ValidateShirtNumber(player);
            if (!ModelState.IsValid)
            {
                await LoadFormLists(player.TeamId);
                return View(player);
            }

            player.FullName = player.FullName.Trim();
            player.UpdatedAt = DateTime.Now;
            _db.Players.Add(player);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Đã thêm cầu thủ \"{player.FullName}\".";
            return RedirectToAction(nameof(Details), new { id = player.Id });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var player = await _db.Players.FindAsync(id);
            if (player == null) return NotFound();
            await LoadFormLists(player.TeamId);
            return View(player);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Player input)
        {
            if (id != input.Id) return BadRequest();
            await ValidateShirtNumber(input);
            if (!ModelState.IsValid)
            {
                await LoadFormLists(input.TeamId);
                return View(input);
            }

            var p = await _db.Players.FindAsync(id);
            if (p == null) return NotFound();

            p.FullName = input.FullName.Trim();
            p.DateOfBirth = input.DateOfBirth;
            p.Nationality = input.Nationality.Trim();
            p.Hometown = input.Hometown;
            p.Position = input.Position;
            p.PreferredFoot = input.PreferredFoot;
            p.HeightCm = input.HeightCm;
            p.WeightKg = input.WeightKg;
            p.ShirtNumber = input.ShirtNumber;
            p.TeamId = input.TeamId;
            p.Status = input.Status;
            p.MarketValue = input.MarketValue;
            p.PhotoUrl = input.PhotoUrl;
            // HLV/Admin chỉnh chỉ số năng lực => không còn là giá trị ước tính
            if (p.Pace != input.Pace || p.Shooting != input.Shooting || p.Passing != input.Passing ||
                p.Dribbling != input.Dribbling || p.Defending != input.Defending || p.Physical != input.Physical)
                p.AttributesEstimated = false;
            p.Pace = input.Pace;
            p.Shooting = input.Shooting;
            p.Passing = input.Passing;
            p.Dribbling = input.Dribbling;
            p.Defending = input.Defending;
            p.Physical = input.Physical;
            p.Notes = input.Notes;
            p.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Đã cập nhật cầu thủ \"{p.FullName}\".";
            return RedirectToAction(nameof(Details), new { id });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await _db.Players.AsNoTracking().Include(x => x.Team).FirstOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();
            ViewBag.StatCount = await _db.PlayerStats.CountAsync(s => s.PlayerId == id);
            return View(p);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var p = await _db.Players.FindAsync(id);
            if (p == null) return NotFound();

            _db.Players.Remove(p); // thống kê của cầu thủ bị xóa theo (cascade)
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã xóa cầu thủ \"{p.FullName}\".";
            return RedirectToAction(nameof(Index));
        }

        // ---------------------------------------------------------------
        private async Task ValidateShirtNumber(Player p)
        {
            if (p.TeamId == null || p.ShirtNumber == null) return;
            var taken = await _db.Players.AnyAsync(x =>
                x.TeamId == p.TeamId && x.ShirtNumber == p.ShirtNumber && x.Id != p.Id);
            if (taken)
                ModelState.AddModelError(nameof(Player.ShirtNumber), $"Số áo {p.ShirtNumber} đã có cầu thủ khác trong đội sử dụng");
        }

        private async Task LoadFormLists(int? teamId)
        {
            ViewBag.Teams = new SelectList(await _db.Teams.OrderBy(t => t.Name).ToListAsync(), "Id", "Name", teamId);
            ViewBag.Nationalities = await _db.Players.Select(p => p.Nationality).Distinct().OrderBy(n => n).ToListAsync();
        }

        private async Task LoadFilterLists()
        {
            ViewBag.Teams = await _db.Teams.OrderBy(t => t.Name)
                .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name }).ToListAsync();
            ViewBag.Nationalities = await _db.Players.Select(p => p.Nationality).Distinct().OrderBy(n => n).ToListAsync();
        }
    }
}
