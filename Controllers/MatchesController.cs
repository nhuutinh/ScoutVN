using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>Quản lý trận đấu (lịch thi đấu và kết quả) – nguồn dữ liệu để tính bảng xếp hạng đội.</summary>
    [Authorize]
    public class MatchesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;

        public MatchesController(AppDbContext db, IAnalysisService analysis)
        {
            _db = db;
            _analysis = analysis;
        }

        // GET /Matches?tournamentId=&round=&teamId=
        public async Task<IActionResult> Index(int? tournamentId, int? round, int? teamId)
        {
            tournamentId ??= await _analysis.DefaultTournamentIdAsync();
            var q = _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam).Include(m => m.Tournament).AsQueryable();
            if (tournamentId.HasValue) q = q.Where(m => m.TournamentId == tournamentId);
            if (round.HasValue) q = q.Where(m => m.Round == round);
            if (teamId.HasValue) q = q.Where(m => m.HomeTeamId == teamId || m.AwayTeamId == teamId);
            var list = await q.OrderBy(m => m.Round).ThenBy(m => m.KickOff).ToListAsync();

            var tours = await _db.Tournaments.AsNoTracking().OrderByDescending(t => t.Season).ToListAsync();
            ViewBag.Tournaments = tours.Select(t => new SelectListItem($"{t.Name} {t.Season}", t.Id.ToString(), t.Id == tournamentId)).ToList();
            var rounds = await _db.Matches.Where(m => m.TournamentId == tournamentId).Select(m => m.Round).Distinct().ToListAsync();
            ViewBag.Rounds = rounds.OrderBy(r => r).Select(r => new SelectListItem($"Vòng {r}", r.ToString(), r == round)).ToList();
            ViewBag.Teams = (await _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync())
                .Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == teamId)).ToList();
            ViewBag.TournamentId = tournamentId;
            ViewBag.Round = round;
            ViewBag.TeamId = teamId;
            return View(list);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int? tournamentId, int? round)
        {
            var m = new Match { TournamentId = tournamentId ?? 0, Round = round ?? 1, KickOff = DateTime.Today.AddHours(18) };
            await LoadLists(m);
            return View("Form", m);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Match m)
        {
            await ValidateMatch(m);
            if (!ModelState.IsValid) { await LoadLists(m); return View("Form", m); }
            _db.Matches.Add(m);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã thêm trận đấu.";
            return RedirectToAction(nameof(Index), new { tournamentId = m.TournamentId, round = m.Round });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var m = await _db.Matches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m == null) return NotFound();
            await LoadLists(m);
            return View("Form", m);
        }

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Match input)
        {
            if (id != input.Id) return BadRequest();
            await ValidateMatch(input);
            if (!ModelState.IsValid) { await LoadLists(input); return View("Form", input); }
            var m = await _db.Matches.FindAsync(id);
            if (m == null) return NotFound();
            m.TournamentId = input.TournamentId; m.Round = input.Round; m.KickOff = input.KickOff;
            m.HomeTeamId = input.HomeTeamId; m.AwayTeamId = input.AwayTeamId;
            m.HomeGoals = input.HomeGoals; m.AwayGoals = input.AwayGoals;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã cập nhật trận đấu – bảng xếp hạng được tính lại tự động.";
            return RedirectToAction(nameof(Index), new { tournamentId = m.TournamentId, round = m.Round });
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var m = await _db.Matches.AsNoTracking().Include(x => x.HomeTeam).Include(x => x.AwayTeam).Include(x => x.Tournament)
                .FirstOrDefaultAsync(x => x.Id == id);
            return m == null ? NotFound() : View(m);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var m = await _db.Matches.FindAsync(id);
            if (m == null) return NotFound();
            _db.Matches.Remove(m);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Đã xóa trận đấu.";
            return RedirectToAction(nameof(Index), new { tournamentId = m.TournamentId, round = m.Round });
        }

        /// <summary>Mỗi đội chỉ đá tối đa 1 trận trong một vòng của cùng giải.</summary>
        private async Task ValidateMatch(Match m)
        {
            var clash = await _db.Matches.AnyAsync(x => x.Id != m.Id && x.TournamentId == m.TournamentId && x.Round == m.Round &&
                (x.HomeTeamId == m.HomeTeamId || x.AwayTeamId == m.HomeTeamId || x.HomeTeamId == m.AwayTeamId || x.AwayTeamId == m.AwayTeamId));
            if (clash) ModelState.AddModelError(nameof(Match.Round), "Một trong hai đội đã có trận khác ở vòng này của giải.");
        }

        private async Task LoadLists(Match m)
        {
            var tours = await _db.Tournaments.AsNoTracking().OrderByDescending(t => t.Season).ToListAsync();
            ViewBag.Tournaments = tours.Select(t => new SelectListItem($"{t.Name} {t.Season}", t.Id.ToString(), t.Id == m.TournamentId)).ToList();
            var teams = await _db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
            ViewBag.Teams = teams.Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToList();
        }
    }
}
