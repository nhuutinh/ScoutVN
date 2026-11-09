using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>
    /// Cổng thông tin công khai: KHÔNG cần đăng nhập.
    /// Hiển thị trang chủ, giải đấu, bảng xếp hạng cá nhân, tin tức, hồ sơ cơ bản cầu thủ / đội bóng.
    /// Các chức năng chuyên sâu (phân tích, so sánh, xếp hạng theo tiêu chí, gợi ý AI) vẫn yêu cầu đăng nhập.
    /// </summary>
    [AllowAnonymous]
    public class PortalController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;

        public PortalController(AppDbContext db, IAnalysisService analysis)
        {
            _db = db;
            _analysis = analysis;
        }

        // ================= Trang chủ =================
        [HttpGet("")]
        [HttpGet("trang-chu")]
        public async Task<IActionResult> Home()
        {
            var leagueId = await _analysis.DefaultTournamentIdAsync();
            var league = leagueId == null ? null : await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == leagueId);
            var rows = leagueId == null ? new List<PlayerRow>() : await _analysis.LoadRowsAsync(leagueId);
            var withStats = rows.Where(r => r.Stat != null).ToList();

            var news = _db.News.AsNoTracking().Where(n => n.IsPublished && n.PublishedAt <= DateTime.Now);
            var featured = await news.Where(n => n.IsFeatured).OrderByDescending(n => n.PublishedAt).Take(3).ToListAsync();
            var featuredIds = featured.Select(n => n.Id).ToList();
            var latest = await news.Where(n => !featuredIds.Contains(n.Id)).OrderByDescending(n => n.PublishedAt).Take(6).ToListAsync();

            var vm = new PortalHomeViewModel
            {
                PlayerCount = await _db.Players.CountAsync(p => p.Status != PlayerStatus.Retired),
                TeamCount = await _db.Teams.CountAsync(),
                TournamentCount = await _db.Tournaments.CountAsync(),
                // Tổng bàn lấy từ kết quả trận (gồm cả bàn phản lưới nhà)
                SeasonGoals = leagueId == null ? 0 : await _db.Matches.Where(m => m.TournamentId == leagueId && m.HomeGoals != null)
                    .SumAsync(m => (int?)(m.HomeGoals!.Value + m.AwayGoals!.Value)) ?? 0,
                CurrentLeague = league,
                Standings = leagueId == null ? new() : await _analysis.StandingsAsync(leagueId.Value),
                Featured = featured,
                Latest = latest,
                Leaders = BuildLeaders(withStats, 10),
                Highlights = BuildHighlights(rows, withStats, league, leagueId == null ? new() : await _analysis.StandingsAsync(leagueId.Value)),
                Tournaments = await BuildTournamentCards(4),
                Teams = await BuildTeamCards(withStats)
            };
            return View(vm);
        }

        // ================= Giải đấu =================
        [HttpGet("giai-dau")]
        public async Task<IActionResult> Tournaments()
            => View(await BuildTournamentCards(100));

        [HttpGet("giai-dau/{id:int}")]
        public async Task<IActionResult> Tournament(int id)
        {
            var t = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return NotFound();
            var rows = (await _analysis.LoadRowsAsync(id, excludeRetired: false)).Where(r => r.Stat != null).ToList();
            var matches = await _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam)
                .Where(m => m.TournamentId == id).OrderBy(m => m.Round).ThenBy(m => m.KickOff).ToListAsync();
            var standings = await _analysis.StandingsAsync(id);
            var vm = new PortalTournamentViewModel
            {
                Tournament = t,
                Players = rows.Count,
                MatchesPlayed = matches.Count(m => m.IsPlayed),
                Goals = matches.Where(m => m.IsPlayed).Sum(m => m.HomeGoals!.Value + m.AwayGoals!.Value),
                YellowCards = rows.Sum(r => r.Stat!.YellowCards),
                Leaders = BuildLeaders(rows, 20),
                Standings = standings,
                Matches = matches,
                GoalsByTeam = standings.OrderByDescending(r => r.GoalsFor)
                    .Select(r => new ChartPoint(r.Team.ShortName ?? r.Team.Name, r.GoalsFor)).ToList(),
                RelatedNews = await _db.News.AsNoTracking()
                    .Where(n => n.IsPublished && n.TournamentId == id && n.PublishedAt <= DateTime.Now)
                    .OrderByDescending(n => n.PublishedAt).Take(4).ToListAsync()
            };
            return View(vm);
        }

        // ================= Tin tức =================
        [HttpGet("tin-tuc")]
        public async Task<IActionResult> News(string? q, NewsCategory? category, int page = 1)
        {
            var news = _db.News.AsNoTracking().Where(n => n.IsPublished && n.PublishedAt <= DateTime.Now);
            var filtered = news;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var kw = q.Trim();
                filtered = filtered.Where(n => EF.Functions.Collate(n.Title, AppDbContext.SearchCollation).Contains(kw)
                                            || EF.Functions.Collate(n.Summary, AppDbContext.SearchCollation).Contains(kw));
            }
            var counts = (await filtered.Select(n => n.Category).ToListAsync())
                .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            if (category.HasValue) filtered = filtered.Where(n => n.Category == category);
            return View(new PortalNewsListViewModel
            {
                Counts = counts,
                Keyword = q,
                Category = category,
                Items = await PagedList<News>.CreateAsync(filtered.OrderByDescending(n => n.PublishedAt), page, 9),
                MostRead = await news.OrderByDescending(n => n.ViewCount).ThenByDescending(n => n.PublishedAt).Take(5).ToListAsync()
            });
        }

        [HttpGet("tin-tuc/{id:int}/{slug?}")]
        public async Task<IActionResult> NewsDetail(int id)
        {
            var n = await _db.News.Include(x => x.Tournament).FirstOrDefaultAsync(x => x.Id == id);
            // Bản nháp chỉ Admin xem trước được
            if (n == null || (!n.IsPublished && !User.IsInRole("Admin"))) return NotFound();
            if (n.IsPublished)
            {
                n.ViewCount++;
                await _db.SaveChangesAsync();
            }
            var related = await _db.News.AsNoTracking()
                .Where(x => x.IsPublished && x.Id != id && x.PublishedAt <= DateTime.Now
                            && (x.Category == n.Category || (n.TournamentId != null && x.TournamentId == n.TournamentId)))
                .OrderByDescending(x => x.PublishedAt).Take(4).ToListAsync();
            if (related.Count < 3)
            {
                // Bổ sung bằng các tin mới nhất khác
                var ids = related.Select(x => x.Id).Append(id).ToList();
                related.AddRange(await _db.News.AsNoTracking()
                    .Where(x => x.IsPublished && !ids.Contains(x.Id) && x.PublishedAt <= DateTime.Now)
                    .OrderByDescending(x => x.PublishedAt).Take(3 - related.Count).ToListAsync());
            }
            return View(new PortalNewsDetailViewModel { News = n, Related = related });
        }

        // ================= Đội bóng & cầu thủ (hồ sơ cơ bản) =================
        [HttpGet("doi-bong")]
        public async Task<IActionResult> Teams()
        {
            var leagueId = await _analysis.DefaultTournamentIdAsync();
            var rows = leagueId == null ? new List<PlayerRow>() : (await _analysis.LoadRowsAsync(leagueId)).Where(r => r.Stat != null).ToList();
            return View(await BuildTeamCards(rows));
        }

        [HttpGet("doi-bong/{id:int}")]
        public async Task<IActionResult> Team(int id, [FromServices] IClubValuationService valuation)
        {
            var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (team == null) return NotFound();
            var leagueId = await _analysis.DefaultTournamentIdAsync();
            var squad = await _analysis.LoadRowsAsync(leagueId, teamId: id);
            return View(new PortalTeamViewModel
            {
                Team = team,
                League = leagueId == null ? null : await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == leagueId),
                Squad = squad.OrderBy(r => r.Player.Position).ThenByDescending(r => r.Player.Overall).ToList(),
                Value = await valuation.ForTeamAsync(id)
            });
        }

        [HttpGet("cau-thu/{id:int}")]
        public async Task<IActionResult> Player(int id)
        {
            var p = await _db.Players.AsNoTracking()
                .Include(x => x.Team).Include(x => x.Stats).ThenInclude(s => s.Tournament)
                .Include(x => x.Stats).ThenInclude(s => s.Team)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (p == null) return NotFound();
            p.Stats = p.Stats
                .OrderByDescending(s => s.Tournament!.Season).ThenBy(s => s.Tournament!.Type).ToList();
            ViewBag.Insights = await _analysis.InsightsAsync(p);
            return View(p);
        }

        // ---------------------------------------------------------------
        private static Leaderboards BuildLeaders(List<PlayerRow> rows, int take)
        {
            LeaderRow L(PlayerRow r, string v, double raw) => new()
            { Player = r.Player, Appearances = r.Stat!.Appearances, Minutes = r.Stat.Minutes, Value = v, Raw = raw };
            return new Leaderboards
            {
                Goals = rows.Where(r => r.Stat!.Goals > 0).OrderByDescending(r => r.Stat!.Goals).ThenBy(r => r.Stat!.Minutes)
                    .Take(take).Select(r => L(r, r.Stat!.PenaltyGoals > 0 ? $"{r.Stat.Goals} ({r.Stat.PenaltyGoals} pen)" : r.Stat.Goals.ToString(), r.Stat.Goals)).ToList(),
                Minutes = rows.Where(r => r.Stat!.Minutes > 0).OrderByDescending(r => r.Stat!.Minutes)
                    .Take(take).Select(r => L(r, r.Stat!.Minutes.ToString("N0", ScoutVN.Services.Fmt.VI), r.Stat.Minutes)).ToList(),
                Cards = rows.Where(r => r.Stat!.YellowCards + r.Stat.RedCards > 0).OrderByDescending(r => r.Stat!.YellowCards + 2 * r.Stat.RedCards).ThenBy(r => r.Player.FullName)
                    .Take(take).Select(r => L(r, $"{r.Stat!.YellowCards}V / {r.Stat.RedCards}Đ", r.Stat.YellowCards + 2 * r.Stat.RedCards)).ToList(),
                CleanSheets = rows.Where(r => r.Player.Position == Position.GK && r.Stat!.Appearances > 0)
                    .OrderByDescending(r => r.Stat!.CleanSheets).ThenBy(r => r.Stat!.GoalsConceded)
                    .Take(take).Select(r => L(r, r.Stat!.CleanSheets.ToString(), r.Stat.CleanSheets)).ToList()
            };
        }

        private List<Highlight> BuildHighlights(List<PlayerRow> all, List<PlayerRow> withStats, Tournament? league, List<StandingRow> standings)
        {
            var list = new List<Highlight>();
            string P(int id) => Url.Action(nameof(Player), new { id })!;
            string T(int id) => Url.Action(nameof(Team), new { id })!;
            var scope = league == null ? "" : $"{league.Name} {league.Season}";
            var leader = standings.FirstOrDefault();
            if (leader != null) list.Add(new("bi-trophy-fill", "Đội dẫn đầu", leader.Team.Name, $"{leader.Points} điểm", $"{leader.Won}T {leader.Drawn}H {leader.Lost}B sau {leader.Played} trận", T(leader.Team.Id)));
            var s = withStats.OrderByDescending(r => r.Stat!.Goals).ThenBy(r => r.Stat!.Minutes).FirstOrDefault();
            if (s != null) list.Add(new("bi-bullseye", "Dẫn đầu danh sách ghi bàn", s.Player.FullName, $"{s.Stat!.Goals} bàn", $"{s.Player.Team?.Name ?? "–"} · {scope}", P(s.Player.Id)));
            var gk = withStats.Where(x => x.Player.Position == Position.GK).OrderByDescending(x => x.Stat!.CleanSheets).ThenBy(x => x.Stat!.GoalsConceded).FirstOrDefault();
            if (gk != null) list.Add(new("bi-shield-check", "Người gác đền", gk.Player.FullName, $"{gk.Stat!.CleanSheets} trận sạch lưới", $"{gk.Stat.GoalsConceded} bàn thua / {gk.Stat.Appearances} trận", P(gk.Player.Id)));
            var attack = standings.OrderByDescending(x => x.GoalsFor).FirstOrDefault();
            if (attack != null) list.Add(new("bi-fire", "Hàng công mạnh nhất", attack.Team.Name, $"{attack.GoalsFor} bàn", $"{attack.Played} trận · {scope}", T(attack.Team.Id)));
            var defence = standings.OrderBy(x => x.GoalsAgainst).ThenByDescending(x => x.Points).FirstOrDefault();
            if (defence != null) list.Add(new("bi-bricks", "Hàng thủ chắc chắn nhất", defence.Team.Name, $"{defence.GoalsAgainst} bàn thua", $"{defence.Played} trận · {scope}", T(defence.Team.Id)));
            var young = withStats.Where(x => x.Player.Age <= 21 && x.Stat!.Minutes > 0).OrderByDescending(x => x.Stat!.Minutes).FirstOrDefault();
            if (young != null) list.Add(new("bi-rocket-takeoff", "Cầu thủ trẻ được tin dùng", young.Player.FullName, $"{young.Stat!.Minutes} phút", $"{young.Player.Age} tuổi · {young.Player.Team?.Name ?? "–"}", P(young.Player.Id)));
            return list;
        }

        private async Task<List<TournamentCard>> BuildTournamentCards(int take)
        {
            var ts = await _db.Tournaments.AsNoTracking()
                .OrderByDescending(t => t.Season).ThenBy(t => t.Type).ThenBy(t => t.Name).Take(take).ToListAsync();
            var ids = ts.Select(t => t.Id).ToList();
            var stats = await _db.PlayerStats.AsNoTracking().Include(s => s.Player)
                .Where(s => ids.Contains(s.TournamentId)).ToListAsync();
            return ts.Select(t =>
            {
                var st = stats.Where(s => s.TournamentId == t.Id).ToList();
                var top = st.OrderByDescending(s => s.Goals).ThenBy(s => s.MinutesPlayed).FirstOrDefault();
                return new TournamentCard
                {
                    Tournament = t, Players = st.Count, Goals = st.Sum(s => s.Goals),
                    TopScorer = top?.Player?.FullName, TopScorerGoals = top?.Goals ?? 0, TopScorerId = top?.PlayerId
                };
            }).ToList();
        }

        private async Task<List<TeamCard>> BuildTeamCards(List<PlayerRow> leagueRows)
        {
            var teams = await _db.Teams.AsNoTracking().Include(t => t.Players).OrderBy(t => t.Name).ToListAsync();
            // Ưu tiên các đội đang thi đấu ở giải hiện tại
            var active = leagueRows.Where(r => r.Player.TeamId != null).Select(r => r.Player.TeamId!.Value).ToHashSet();
            if (active.Count > 0) teams = teams.OrderBy(t => active.Contains(t.Id) ? 0 : 1).ThenBy(t => t.Name).ToList();
            return teams.Select(t => new TeamCard
            {
                Team = t,
                Players = t.Players.Count(p => p.Status != PlayerStatus.Retired),
                AvgOverall = t.Players.Any() ? t.Players.Average(p => p.Overall) : 0,
                Goals = leagueRows.Where(r => r.Player.TeamId == t.Id).Sum(r => r.Stat?.Goals ?? 0)
            }).ToList();
        }
    }
}
