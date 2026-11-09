using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý dữ liệu thống kê thi đấu của cầu thủ theo từng giải đấu.</summary>
    [Authorize]
    public class PlayerStatsController : Controller
    {
        private readonly AppDbContext _db;

        public PlayerStatsController(AppDbContext db) => _db = db;

        public async Task<IActionResult> Index(string? keyword, int? tournamentId, int? teamId, string? sort, int page = 1)
        {
            var query = _db.PlayerStats.AsNoTracking()
                .Include(s => s.Player)
                .Include(s => s.Team)
                .Include(s => s.Tournament)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(s => EF.Functions.Collate(s.Player!.FullName, AppDbContext.SearchCollation).Contains(kw));
            }
            if (tournamentId.HasValue) query = query.Where(s => s.TournamentId == tournamentId);
            if (teamId.HasValue) query = query.Where(s => s.TeamId == teamId);

            query = sort switch
            {
                "minutes" => query.OrderByDescending(s => s.MinutesPlayed).ThenByDescending(s => s.Goals),
                "apps" => query.OrderByDescending(s => s.Appearances).ThenByDescending(s => s.Starts),
                "cards" => query.OrderByDescending(s => s.YellowCards + 2 * s.RedCards),
                "cs" => query.OrderByDescending(s => s.CleanSheets).ThenBy(s => s.GoalsConceded),
                "name" => query.OrderBy(s => s.Player!.FullName),
                _ => query.OrderByDescending(s => s.Goals).ThenBy(s => s.MinutesPlayed)
            };

            await LoadFilterLists(tournamentId, teamId);
            return View(new PlayerStatIndexViewModel
            {
                Keyword = keyword,
                TournamentId = tournamentId,
                TeamId = teamId,
                Sort = sort,
                Stats = await PagedList<PlayerStat>.CreateAsync(query, page, 20)
            });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int? playerId, int? tournamentId)
        {
            var player = playerId.HasValue ? await _db.Players.FindAsync(playerId.Value) : null;
            var stat = new PlayerStat { PlayerId = playerId ?? 0, TournamentId = tournamentId ?? 0, TeamId = player?.TeamId };
            await LoadFormLists(stat);
            return View(stat);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(PlayerStat stat)
        {
            await ValidateUnique(stat);
            if (!ModelState.IsValid)
            {
                await LoadFormLists(stat);
                return View(stat);
            }

            _db.PlayerStats.Add(stat);
            await TouchPlayer(stat.PlayerId);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã thêm thống kê thi đấu.";
            return RedirectToAction("Details", "Players", new { id = stat.PlayerId });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var stat = await _db.PlayerStats.FindAsync(id);
            if (stat == null) return NotFound();
            await LoadFormLists(stat);
            return View(stat);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, PlayerStat input)
        {
            if (id != input.Id) return BadRequest();
            await ValidateUnique(input);
            if (!ModelState.IsValid)
            {
                await LoadFormLists(input);
                return View(input);
            }

            var s = await _db.PlayerStats.FindAsync(id);
            if (s == null) return NotFound();

            s.PlayerId = input.PlayerId;
            s.TournamentId = input.TournamentId;
            s.TeamId = input.TeamId;
            s.Appearances = input.Appearances;
            s.Starts = input.Starts;
            s.MinutesPlayed = input.MinutesPlayed;
            s.Goals = input.Goals;
            s.PenaltyGoals = input.PenaltyGoals;
            s.CleanSheets = input.CleanSheets;
            s.GoalsConceded = input.GoalsConceded;
            s.YellowCards = input.YellowCards;
            s.RedCards = input.RedCards;
            await TouchPlayer(s.PlayerId);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Đã cập nhật thống kê thi đấu.";
            return RedirectToAction("Details", "Players", new { id = s.PlayerId });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var s = await _db.PlayerStats.AsNoTracking()
                .Include(x => x.Player).Include(x => x.Tournament)
                .FirstOrDefaultAsync(x => x.Id == id);
            return s == null ? NotFound() : View(s);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var s = await _db.PlayerStats.FindAsync(id);
            if (s == null) return NotFound();
            var playerId = s.PlayerId;
            _db.PlayerStats.Remove(s);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xóa thống kê thi đấu.";
            return RedirectToAction("Details", "Players", new { id = playerId });
        }

        // ---------------------------------------------------------------
        private async Task ValidateUnique(PlayerStat s)
        {
            if (s.PlayerId <= 0 || s.TournamentId <= 0) return;
            if (await _db.PlayerStats.AnyAsync(x => x.PlayerId == s.PlayerId && x.TournamentId == s.TournamentId && x.Id != s.Id))
                ModelState.AddModelError(nameof(PlayerStat.TournamentId), "Cầu thủ này đã có thống kê ở giải đấu đã chọn – hãy sửa bản ghi cũ");
        }

        private async Task TouchPlayer(int playerId)
        {
            var p = await _db.Players.FindAsync(playerId);
            if (p != null) p.UpdatedAt = DateTime.Now;
        }

        private async Task LoadFormLists(PlayerStat s)
        {
            var players = await _db.Players.Include(p => p.Team).OrderBy(p => p.FullName).ToListAsync();
            ViewBag.Players = players.Select(p => new SelectListItem(
                $"{p.FullName} – {(p.Team != null ? p.Team.Name : "Tự do")} ({p.Position})",
                p.Id.ToString(), p.Id == s.PlayerId)).ToList();
            var tournaments = await _db.Tournaments.OrderByDescending(t => t.Season).ThenBy(t => t.Name).ToListAsync();
            ViewBag.Tournaments = tournaments.Select(t => new SelectListItem(
                $"{t.Name} {t.Season}", t.Id.ToString(), t.Id == s.TournamentId)).ToList();
            var teams = await _db.Teams.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Teams = teams.Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == s.TeamId)).ToList();
        }

        private async Task LoadFilterLists(int? tournamentId, int? teamId)
        {
            var tournaments = await _db.Tournaments.OrderByDescending(t => t.Season).ThenBy(t => t.Name).ToListAsync();
            ViewBag.Tournaments = tournaments.Select(t => new SelectListItem(
                $"{t.Name} {t.Season}", t.Id.ToString(), t.Id == tournamentId)).ToList();
            var teams = await _db.Teams.OrderBy(t => t.Name).ToListAsync();
            ViewBag.Teams = teams.Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == teamId)).ToList();
        }
    }
}
