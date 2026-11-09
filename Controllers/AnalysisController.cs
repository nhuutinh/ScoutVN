using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>Tuần 5: thống kê – phân tích hiệu suất, so sánh, xếp hạng cầu thủ và trực quan hóa.</summary>
    [Authorize]
    public class AnalysisController : Controller
    {
        private const int MaxCompare = 4;
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;

        public AnalysisController(AppDbContext db, IAnalysisService analysis)
        {
            _db = db;
            _analysis = analysis;
        }

        // ================================================================
        // GET /Analysis?tournamentId=  – Tổng quan thống kê theo giải
        // ================================================================
        public async Task<IActionResult> Index(int? tournamentId, [FromServices] IClubValuationService valuation)
        {
            var (tid, tournaments, scope) = await ResolveScope(tournamentId);
            var rows = await _analysis.LoadRowsAsync(tid);
            var withStats = rows.Where(r => r.Stat != null).ToList();
            var totalMin = withStats.Sum(r => r.Stat!.Minutes);

            var hasMinutes = withStats.Any(r => r.Stat!.Minutes > 0);
            var matches = await _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam)
                .Where(m => (tid == 0 || m.TournamentId == tid) && m.HomeGoals != null).ToListAsync();

            var vm = new AnalysisOverviewViewModel
            {
                TournamentId = tid,
                ScopeName = scope,
                Tournaments = tournaments,
                HasMinutes = hasMinutes,
                PlayersWithStats = withStats.Count,
                MatchesPlayed = matches.Count,
                TotalGoals = matches.Sum(m => m.HomeGoals!.Value + m.AwayGoals!.Value),
                HomeWinPct = matches.Count == 0 ? 0 : matches.Count(m => m.HomeGoals > m.AwayGoals) * 100.0 / matches.Count,
                DrawPct = matches.Count == 0 ? 0 : matches.Count(m => m.HomeGoals == m.AwayGoals) * 100.0 / matches.Count,
                YellowCards = withStats.Sum(r => r.Stat!.YellowCards),
                RedCards = withStats.Sum(r => r.Stat!.RedCards),
                TopScorers = withStats.Where(r => r.Stat!.Goals > 0).OrderByDescending(r => r.Stat!.Goals).ThenBy(r => r.Stat!.Minutes).Take(10)
                    .Select(r => new ChartPoint(r.Player.FullName, r.Stat!.Goals, r.Player.Id)).ToList(),
                SecondTitle = hasMinutes ? "Top 10 thời gian thi đấu (phút)" : "Top 10 nhận thẻ phạt (vàng + 2 × đỏ)",
                SecondLabel = hasMinutes ? "Số phút" : "Điểm thẻ phạt",
                SecondBoard = (hasMinutes
                        ? withStats.OrderByDescending(r => r.Stat!.Minutes).Select(r => new ChartPoint(r.Player.FullName, r.Stat!.Minutes, r.Player.Id))
                        : withStats.OrderByDescending(r => r.Stat!.YellowCards + 2 * r.Stat.RedCards).Select(r => new ChartPoint(r.Player.FullName, r.Stat!.YellowCards + 2 * r.Stat.RedCards, r.Player.Id)))
                    .Take(10).ToList(),
                // Bàn thắng / bàn thua của từng đội lấy từ kết quả trận đấu (chính xác hơn cộng từ cầu thủ)
                GoalsByTeam = matches.SelectMany(m => new[] { (T: m.HomeTeam!, F: m.HomeGoals!.Value, A: m.AwayGoals!.Value), (T: m.AwayTeam!, F: m.AwayGoals!.Value, A: m.HomeGoals!.Value) })
                    .GroupBy(x => x.T.ShortName ?? x.T.Name)
                    .Select(g => new ChartPoint(g.Key, g.Sum(x => x.F)))
                    .OrderByDescending(p => p.Value).ToList(),
                GoalsByGroup = Enum.GetValues<PositionGroup>()
                    .Select(g => new ChartPoint(PositionGroups.Name(g), withStats.Where(r => PositionGroups.Of(r.Player.Position) == g).Sum(r => r.Stat!.Goals)))
                    .ToList()
            };
            if (tid != 0) vm.Standings = await _analysis.StandingsAsync(tid);

            // Định giá câu lạc bộ (tổng giá trị ước tính đội hình, tỷ VNĐ)
            var values = await valuation.AllAsync();
            var teamNames = (await _db.Teams.AsNoTracking().ToListAsync()).ToDictionary(t => t.Id, t => t.ShortName ?? t.Name);
            vm.ClubValues = values.Values.OrderByDescending(v => v.Total)
                .Select(v => new ChartPoint(teamNames.GetValueOrDefault(v.TeamId, "?"), Math.Round(v.Total / 1000.0, 1), v.TeamId)).ToList();
            // Phân bố độ tuổi
            var ageBins = new (string L, int Min, int Max)[] { ("≤ 20", 0, 20), ("21–23", 21, 23), ("24–26", 24, 26), ("27–29", 27, 29), ("30–32", 30, 32), ("≥ 33", 33, 99) };
            vm.AgeGroups = ageBins.Select(b => new ChartPoint(b.L, rows.Count(r => r.Player.Age >= b.Min && r.Player.Age <= b.Max))).ToList();

            // Cầu thủ nổi bật theo nhóm vị trí: điểm cao nhất theo bộ tiêu chí mặc định của nhóm (giống trang Xếp hạng)
            foreach (var g in Enum.GetValues<PositionGroup>())
            {
                var pool = withStats.Where(r => PositionGroups.Of(r.Player.Position) == g && (!hasMinutes || r.Stat!.Minutes >= KnnQuery.DefaultMinMinutes)).ToList();
                var needs = CoachNeeds.Defaults(g);
                var weights = CoachNeeds.MetricWeights(needs).ToDictionary(kv => kv.Key, kv => kv.Value * 10);
                var best = _analysis.Rank(pool, weights).FirstOrDefault();
                if (best != null) vm.BestByGroup.Add((PositionGroups.Name(g), best.Row, best.Score,
                    string.Join(", ", needs.Select(n => CoachNeeds.Find(n)!.Name))));
            }
            return View(vm);
        }

        // ================================================================
        // GET /Analysis/Player/5?tournamentId=  – Phân tích hiệu suất 1 cầu thủ
        // ================================================================
        public async Task<IActionResult> Player(int id, int? tournamentId)
        {
            var player = await _db.Players.AsNoTracking()
                .Include(p => p.Team).Include(p => p.Stats).ThenInclude(s => s.Tournament)
                .Include(p => p.Stats).ThenInclude(s => s.Team)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (player == null) return NotFound();

            // Mặc định: giải gần nhất mà cầu thủ có số liệu
            if (tournamentId == null)
            {
                var own = player.Stats.Where(s => s.Tournament != null)
                    .OrderBy(s => s.Tournament!.HasMatchDetails && s.Appearances > 0 ? 0 : 1)
                    .ThenBy(s => s.Tournament!.Type == TournamentType.League ? 0 : 1)
                    .ThenByDescending(s => s.Tournament!.Season).FirstOrDefault();
                tournamentId = own?.TournamentId ?? 0;
            }
            var (tid, tournaments, scope) = await ResolveScope(tournamentId);

            var group = PositionGroups.Of(player.Position);
            var pool = await _analysis.LoadRowsAsync(tid, group);
            var all = tid == 0;
            var loaded = (await _analysis.LoadRowsAsync(tid, playerIds: new[] { id }, excludeRetired: false)).FirstOrDefault();
            var row = new PlayerRow { Player = player, Stat = loaded?.Stat };

            const int minMinutes = KnnQuery.DefaultMinMinutes;
            var statPool = pool.Where(r => r.Stat != null && (r.Stat.Minutes >= minMinutes || (row.Stat?.Minutes ?? 0) == 0)).ToList();
            var keys = Metrics.Attributes.Concat(Metrics.KeyMetrics(group)).ToList();
            // Chỉ số năng lực so với toàn bộ cầu thủ cùng nhóm; chỉ số thi đấu so với nhóm có đủ số phút
            var pctAttr = _analysis.Percentiles(row, pool, Metrics.Attributes);
            var pctStat = row.Stat != null ? _analysis.Percentiles(row, statPool, Metrics.KeyMetrics(group)) : new();

            var vm = new PlayerAnalysisViewModel
            {
                Player = player,
                Row = row,
                Group = group,
                TournamentId = tid,
                ScopeName = scope,
                Tournaments = tournaments,
                Attributes = new[] { player.Pace, player.Shooting, player.Passing, player.Dribbling, player.Defending, player.Physical },
                GroupAverage = pool.Count == 0 ? new double[6] : new[]
                {
                    pool.Average(r => r.Player.Pace), pool.Average(r => r.Player.Shooting), pool.Average(r => r.Player.Passing),
                    pool.Average(r => r.Player.Dribbling), pool.Average(r => r.Player.Defending), pool.Average(r => r.Player.Physical)
                }.Select(v => Math.Round(v, 1)).ToArray(),
                PoolSize = statPool.Count,
                MinMinutes = minMinutes,
                Seasons = player.Stats.Where(s => s.Tournament != null)
                    .OrderBy(s => s.Tournament!.Season).ThenBy(s => s.Tournament!.Type)
                    .Select(s => new SeasonPoint
                    {
                        Tournament = $"{s.Tournament!.Name} {s.Tournament.Season}",
                        HasDetails = s.Tournament.HasMatchDetails,
                        Team = s.Team?.ShortName ?? s.Team?.Name,
                        Appearances = s.Appearances, Starts = s.Starts, Goals = s.Goals, PenaltyGoals = s.PenaltyGoals,
                        YellowCards = s.YellowCards, RedCards = s.RedCards, Minutes = s.MinutesPlayed
                    }).ToList()
            };

            foreach (var key in keys)
            {
                var def = Metrics.Find(key)!;
                var pct = pctAttr.TryGetValue(key, out var a) ? a : pctStat.TryGetValue(key, out var b) ? b : null;
                vm.Percentiles.Add(new PercentileItem { Key = key, Name = def.Name, Display = def.Display(def.Value(row)), Percentile = pct });
            }
            vm.Insights = await _analysis.InsightsAsync(player);
            vm.Strengths = vm.Percentiles.Where(p => p.Percentile >= 75).OrderByDescending(p => p.Percentile)
                .Take(4).Select(p => $"{p.Name}: {p.Display} (phân vị {p.Percentile:0} – tốt hơn {p.Percentile:0}% cầu thủ cùng nhóm)").ToList();
            vm.Weaknesses = vm.Percentiles.Where(p => p.Percentile <= 30).OrderBy(p => p.Percentile)
                .Take(4).Select(p => $"{p.Name}: {p.Display} (phân vị {p.Percentile:0})").ToList();
            return View(vm);
        }

        // ================================================================
        // GET /Analysis/Compare?ids=1&ids=2&tournamentId=&format=csv
        // ================================================================
        public async Task<IActionResult> Compare(int[]? ids, int? tournamentId, string? format)
        {
            var (tid, tournaments, scope) = await ResolveScope(tournamentId);
            ids = (ids ?? Array.Empty<int>()).Where(i => i > 0).Distinct().Take(MaxCompare).ToArray();

            var vm = new CompareViewModel { Ids = ids, TournamentId = tid, ScopeName = scope, Tournaments = tournaments };
            var players = await _db.Players.AsNoTracking().Include(p => p.Team)
                .OrderBy(p => p.Team != null ? p.Team.Name : "~").ThenBy(p => p.Position).ThenBy(p => p.FullName)
                .ToListAsync();
            vm.PlayerOptions = players.Select(p => new SelectListItem(
                $"{p.FullName} ({p.Position}, {p.Age} tuổi, chỉ số {p.Overall})", p.Id.ToString())
            { Group = GetGroup(p.Team?.Name ?? "Cầu thủ tự do") }).ToList();
            vm.Picks = players.OrderBy(p => p.FullName).Select(p => new PlayerPick(p.Id, p.FullName,
                $"{p.Position} · {p.Team?.ShortName ?? "Tự do"} · {p.Age} tuổi")).ToList();
            vm.Names = players.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id, p => p.FullName);

            if (ids.Length > 0)
            {
                var rows = await _analysis.LoadRowsAsync(tid, playerIds: ids, excludeRetired: false);
                vm.Rows = ids.Select(i => rows.FirstOrDefault(r => r.Player.Id == i)).Where(r => r != null).Select(r => r!).ToList();
            }
            if (vm.Rows.Count == 1) vm.Message = "Hãy chọn thêm ít nhất 1 cầu thủ để so sánh (tối đa 4).";

            // Chỉ số thi đấu hiển thị: nếu tất cả là thủ môn thì dùng bộ chỉ số thủ môn
            var allGk = vm.Rows.Count > 0 && vm.Rows.All(r => r.Player.Position == Position.GK);
            vm.StatMetricKeys = allGk
                ? new() { "minutes", "minshare", "startrate", "csrate", "gc90", "cards90" }
                : new() { "goals", "minutes", "minshare", "startrate", "goals90", "cards90" };

            if (format == "csv" && vm.Rows.Count > 0) return CompareCsv(vm);
            return View(vm);
        }

        // ================================================================
        // GET /Analysis/Ranking?Group=&TournamentId=&W[pace]=..&format=csv
        // ================================================================
        public async Task<IActionResult> Ranking(PositionGroup? group, int? tournamentId, int? teamId,
            [FromQuery(Name = "n")] string[]? needs, int? minHeight, double? minSpeed, string? foot, string? origin, double? maxValue,
            int? minAge, int? maxAge, int minMinutes = KnnQuery.DefaultMinMinutes, int top = 20, string? format = null)
        {
            var (tid, tournaments, scope) = await ResolveScope(tournamentId);
            // Đã bấm "Xếp hạng" (go=1) mà chưa chọn tiêu chí nào → báo lỗi, không dùng bộ mặc định
            var missing = Request.Query.ContainsKey("go") && CoachNeeds.Valid(needs, group).Count == 0;
            // Không chọn sẵn tiêu chí nào: HLV tự chọn (bấm "Bỏ lọc" cũng trở về trạng thái chưa chọn)
            var list = missing ? new List<string>() : CoachNeeds.Valid(needs, group);
            var weights = CoachNeeds.MetricWeights(list).ToDictionary(kv => kv.Key, kv => kv.Value * 10);
            var filter = new NeedFilter
            {
                Needs = list,
                MinHeight = minHeight is > 0 ? Math.Clamp(minHeight.Value, 150, 210) : null,
                MinSpeed = minSpeed is > 0 ? Math.Clamp(minSpeed.Value, 24, 36) : null,
                Foot = foot is "Left" or "Right" ? foot : null,
                Origin = origin is "vn" or "foreign" ? origin : null,
                MaxValue = maxValue is > 0 ? maxValue : null,
                MinAge = minAge, MaxAge = maxAge
            };

            var vm = new RankingViewModel
            {
                Group = group, TournamentId = tid, TeamId = teamId, MinAge = minAge, MaxAge = maxAge,
                MinMinutes = Math.Clamp(minMinutes, 0, 5000), Top = Math.Clamp(top, 5, 100),
                Weights = weights, Tournaments = tournaments, ScopeName = scope,
                Needs = list, NeedsMissing = missing, Filter = filter, Columns = missing ? new() : CoachNeeds.Columns(list),
                Teams = await _db.Teams.OrderBy(t => t.Name)
                    .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name, Selected = t.Id == teamId }).ToListAsync(),
                ActiveMetrics = weights.Where(w => w.Value > 0).OrderByDescending(w => w.Value)
                    .Select(w => Metrics.Find(w.Key)!).ToList()
            };

            // Chưa bấm "Xếp hạng" hoặc chưa chọn tiêu chí → chưa tính kết quả
            if (missing || (!Request.Query.ContainsKey("go") && format != "csv")) return format == "csv" ? RankingCsv(vm) : View(vm);
            var pool = await _analysis.LoadRowsAsync(tid, group, teamId);
            var before = pool.Count;
            pool = filter.Apply(pool).ToList();
            vm.ExcludedByFilter = before - pool.Count;
            vm.PoolSize = pool.Count;
            // Có tiêu chí thống kê => chỉ xét cầu thủ đủ số phút thi đấu để tránh mẫu quá nhỏ
            if (vm.ActiveMetrics.Any(m => m.IsStat) && pool.Any(r => r.Stat?.Minutes > 0))
            {
                var b2 = pool.Count;
                pool = pool.Where(r => r.Stat != null && r.Stat.Minutes >= vm.MinMinutes).ToList();
                vm.ExcludedForMinutes = b2 - pool.Count;
            }
            vm.Results = _analysis.Rank(pool, weights).Take(vm.Top).ToList();

            if (format == "csv") return RankingCsv(vm);
            return View(vm);
        }

        // ---------------------------------------------------------------
        private readonly Dictionary<string, SelectListGroup> _groups = new();
        private SelectListGroup GetGroup(string name)
        {
            if (!_groups.TryGetValue(name, out var g)) _groups[name] = g = new SelectListGroup { Name = name };
            return g;
        }

        /// <summary>Xác định phạm vi số liệu: một giải cụ thể hoặc 0 = tổng hợp tất cả giải.</summary>
        private async Task<(int Id, List<SelectListItem> Items, string Name)> ResolveScope(int? tournamentId)
        {
            var list = await _analysis.TournamentsWithStatsAsync();
            int id = tournamentId ?? await _analysis.DefaultTournamentIdAsync() ?? 0;
            if (id != 0 && list.All(t => t.Id != id)) id = 0;
            var items = new List<SelectListItem> { new("Tất cả giải (tổng hợp)", "0", id == 0) };
            items.AddRange(list.Select(t => new SelectListItem($"{t.Name} {t.Season}", t.Id.ToString(), t.Id == id)));
            var name = id == 0 ? "Tất cả giải (tổng hợp)" : items.First(i => i.Value == id.ToString()).Text;
            return (id, items, name);
        }

        private static string Csv(string? s)
        {
            s ??= "";
            return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
        private static string Num(double? v, string fmt = "0.##") => v.HasValue ? v.Value.ToString(fmt, CultureInfo.InvariantCulture) : "";

        private FileContentResult CsvFile(StringBuilder sb, string name)
        {
            // BOM UTF-8 để Excel hiển thị đúng tiếng Việt
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private FileContentResult RankingCsv(RankingViewModel vm)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Xếp hạng cầu thủ – {Csv(vm.ScopeName)} – nhu cầu: {Csv(string.Join("; ", vm.Needs.Select(k => CoachNeeds.Find(k)?.Name)))}");
            sb.Append("Hạng,Cầu thủ,Đội,Vị trí,Tuổi");
            foreach (var c in vm.Columns) sb.Append(',').Append(Csv(c.Header));
            sb.AppendLine(",Điểm phù hợp nhu cầu");
            int i = 1;
            foreach (var r in vm.Results)
            {
                var p = r.Row.Player;
                sb.Append($"{i++},{Csv(p.FullName)},{Csv(p.Team?.Name ?? "Tự do")},{p.Position},{p.Age}");
                foreach (var c in vm.Columns) sb.Append(',').Append(Csv(c.Show(r.Row)));
                sb.AppendLine("," + Num(r.Score, "0.0"));
            }
            return CsvFile(sb, "XepHang");
        }

        private FileContentResult CompareCsv(CompareViewModel vm)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"So sánh cầu thủ – {Csv(vm.ScopeName)}");
            sb.AppendLine("Tiêu chí," + string.Join(",", vm.Rows.Select(r => Csv(r.Player.FullName))));
            sb.AppendLine("Đội," + string.Join(",", vm.Rows.Select(r => Csv(r.Player.Team?.Name ?? "Tự do"))));
            sb.AppendLine("Vị trí," + string.Join(",", vm.Rows.Select(r => r.Player.Position.ToString())));
            sb.AppendLine("Tuổi," + string.Join(",", vm.Rows.Select(r => r.Player.Age.ToString())));
            foreach (var key in Metrics.Attributes.Append("ovr").Append("height").Append("value").Concat(vm.StatMetricKeys))
            {
                var m = Metrics.Find(key)!;
                sb.AppendLine(Csv(m.Name) + "," + string.Join(",", vm.Rows.Select(r => Num(m.Value(r)))));
            }
            return CsvFile(sb, "SoSanh");
        }
    }
}
