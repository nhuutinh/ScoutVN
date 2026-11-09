using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        public HomeController(AppDbContext db) => _db = db;

        /// <summary>Bảng điều khiển nội bộ (sau khi đăng nhập). Trang chủ công khai nằm ở PortalController.</summary>
        public async Task<IActionResult> Dashboard()
        {
            var vm = new DashboardViewModel
            {
                PlayerCount = await _db.Players.CountAsync(),
                TeamCount = await _db.Teams.CountAsync(),
                TournamentCount = await _db.Tournaments.CountAsync(),
                StatCount = await _db.PlayerStats.CountAsync(),
                UserCount = await _db.Users.CountAsync(),
                TopPlayers = await _db.Players.AsNoTracking().Include(p => p.Team)
                    .OrderByDescending(p => p.Overall).ThenBy(p => p.FullName).Take(5).ToListAsync(),
                RecentlyUpdated = await _db.Players.AsNoTracking().Include(p => p.Team)
                    .OrderByDescending(p => p.UpdatedAt).Take(5).ToListAsync()
            };

            // Vua phá lưới của giải VĐQG mùa mới nhất
            var latestLeague = await _db.Tournaments.AsNoTracking()
                .Where(t => t.Type == TournamentType.League && t.PlayerStats.Any())
                .OrderByDescending(t => t.Season).FirstOrDefaultAsync();
            if (latestLeague != null)
            {
                ViewBag.LatestLeague = $"{latestLeague.Name} {latestLeague.Season}";
                vm.TopScorers = await _db.PlayerStats.AsNoTracking()
                    .Include(s => s.Player).Include(s => s.Team)
                    .Where(s => s.TournamentId == latestLeague.Id && s.Goals > 0)
                    .OrderByDescending(s => s.Goals).ThenBy(s => s.MinutesPlayed)
                    .Take(5).ToListAsync();
            }

            var groups = await _db.Players.GroupBy(p => p.Position)
                .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            vm.PositionGroups = groups.OrderBy(g => g.Key)
                .Select(g => (g.Key.DisplayName(), g.Count)).ToList();

            return View(vm);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
