using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý giải đấu.</summary>
    [Authorize]
    public class TournamentsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;

        public TournamentsController(AppDbContext db, IAnalysisService analysis)
        {
            _db = db;
            _analysis = analysis;
        }

        public async Task<IActionResult> Index(string? keyword, string? season, TournamentType? type, int page = 1)
        {
            var query = _db.Tournaments.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(t =>
                    EF.Functions.Collate(t.Name, AppDbContext.SearchCollation).Contains(kw) ||
                    (t.Organizer != null && EF.Functions.Collate(t.Organizer, AppDbContext.SearchCollation).Contains(kw)));
            }
            if (!string.IsNullOrWhiteSpace(season)) query = query.Where(t => t.Season == season);
            if (type.HasValue) query = query.Where(t => t.Type == type);

            var items = query
                .OrderByDescending(t => t.Season).ThenBy(t => t.Type).ThenBy(t => t.Name)
                .Select(t => new TournamentListItem
                {
                    Tournament = t,
                    PlayerCount = t.PlayerStats.Count,
                    TotalGoals = t.PlayerStats.Sum(s => (int?)s.Goals) ?? 0
                });

            var vm = new TournamentIndexViewModel
            {
                Keyword = keyword,
                Season = season,
                Type = type,
                Seasons = await _db.Tournaments.Select(t => t.Season).Distinct().OrderByDescending(s => s).ToListAsync(),
                Tournaments = await PagedList<TournamentListItem>.CreateAsync(items, page, 10)
            };
            return View(vm);
        }

        public async Task<IActionResult> Details(int id)
        {
            var t = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();

            var stats = _db.PlayerStats.AsNoTracking()
                .Include(s => s.Player).Include(s => s.Team)
                .Where(s => s.TournamentId == id);

            ViewBag.TopScorers = await stats.Where(s => s.Goals > 0).OrderByDescending(s => s.Goals).ThenBy(s => s.MinutesPlayed).Take(10).ToListAsync();
            ViewBag.TopCards = await stats.Where(s => s.YellowCards + s.RedCards > 0).OrderByDescending(s => s.YellowCards + 2 * s.RedCards).ThenBy(s => s.Player!.FullName).Take(10).ToListAsync();
            ViewBag.TopMinutes = t.HasMatchDetails ? await stats.OrderByDescending(s => s.MinutesPlayed).Take(10).ToListAsync() : new List<PlayerStat>();
            ViewBag.PlayerCount = await stats.CountAsync();
            ViewBag.Standings = await _analysis.StandingsAsync(id);
            var matches = await _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam)
                .Where(m => m.TournamentId == id).OrderBy(m => m.Round).ThenBy(m => m.KickOff).ToListAsync();
            ViewBag.Matches = matches;
            ViewBag.TotalGoals = matches.Where(m => m.IsPlayed).Sum(m => m.HomeGoals!.Value + m.AwayGoals!.Value);
            return View(t);
        }

        [Authorize(Roles = "Admin")]
        public IActionResult Create() => View(new Tournament { Season = "2025/26" });

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Tournament tournament)
        {
            await ValidateUnique(tournament);
            if (!ModelState.IsValid) return View(tournament);

            _db.Tournaments.Add(tournament);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã thêm giải đấu \"{tournament.Name} {tournament.Season}\".";
            return RedirectToAction(nameof(Details), new { id = tournament.Id });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var t = await _db.Tournaments.FindAsync(id);
            return t == null ? NotFound() : View(t);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Tournament input)
        {
            if (id != input.Id) return BadRequest();
            await ValidateUnique(input);
            if (!ModelState.IsValid) return View(input);

            var t = await _db.Tournaments.FindAsync(id);
            if (t == null) return NotFound();

            t.Name = input.Name.Trim();
            t.Season = input.Season.Trim();
            t.Type = input.Type;
            t.Organizer = input.Organizer;
            t.StartDate = input.StartDate;
            t.EndDate = input.EndDate;
            t.Description = input.Description;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Đã cập nhật giải đấu.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var t = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            ViewBag.StatCount = await _db.PlayerStats.CountAsync(s => s.TournamentId == id);
            return View(t);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var t = await _db.Tournaments.FindAsync(id);
            if (t == null) return NotFound();

            _db.Tournaments.Remove(t); // thống kê liên quan bị xóa theo (cascade)
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Đã xóa giải đấu \"{t.Name} {t.Season}\".";
            return RedirectToAction(nameof(Index));
        }

        private async Task ValidateUnique(Tournament t)
        {
            if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.Season)) return;
            var name = t.Name.Trim();
            var season = t.Season.Trim();
            if (await _db.Tournaments.AnyAsync(x => x.Name == name && x.Season == season && x.Id != t.Id))
                ModelState.AddModelError(nameof(Tournament.Season), "Giải đấu này đã có trong mùa giải đã chọn");
        }
    }
}
