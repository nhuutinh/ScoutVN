using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;

namespace ScoutVN.Services
{
    /// <summary>Nhóm vị trí dùng cho so sánh "cùng vị trí" và bộ tiêu chí mặc định.</summary>
    public enum PositionGroup { GK = 1, DEF = 2, MID = 3, ATT = 4 }

    public static class PositionGroups
    {
        public static PositionGroup Of(Position p) => p switch
        {
            Position.GK => PositionGroup.GK,
            Position.CB or Position.LB or Position.RB => PositionGroup.DEF,
            Position.DM or Position.CM or Position.AM => PositionGroup.MID,
            _ => PositionGroup.ATT
        };

        public static Position[] Members(PositionGroup g) => g switch
        {
            PositionGroup.GK => new[] { Position.GK },
            PositionGroup.DEF => new[] { Position.CB, Position.LB, Position.RB },
            PositionGroup.MID => new[] { Position.DM, Position.CM, Position.AM },
            _ => new[] { Position.LW, Position.RW, Position.ST }
        };

        public static string Name(PositionGroup g) => g switch
        {
            PositionGroup.GK => "Thủ môn",
            PositionGroup.DEF => "Hậu vệ",
            PositionGroup.MID => "Tiền vệ",
            _ => "Tiền đạo & cánh"
        };
    }

    /// <summary>Số liệu thi đấu đã cộng dồn (một giải hoặc nhiều giải) của một cầu thủ.</summary>
    public class StatAgg
    {
        public int Appearances { get; init; }
        public int Starts { get; init; }
        public int Minutes { get; init; }
        public int Goals { get; init; }
        public int PenaltyGoals { get; init; }
        public int CleanSheets { get; init; }
        public int GoalsConceded { get; init; }
        public int YellowCards { get; init; }
        public int RedCards { get; init; }
        /// <summary>Tổng số trận đội của cầu thủ đã đá trong các giải có số liệu ra sân (mẫu số của tỉ lệ thời gian thi đấu).</summary>
        public int TeamMatches { get; init; }

        /// <summary>Giá trị quy về 90 phút; null nếu không có số phút (giải chỉ có bàn thắng / thẻ phạt).</summary>
        public double? Per90(int value) => Minutes > 0 ? value * 90.0 / Minutes : null;

        /// <param name="teamMatches">Số trận đội đã đá trong giải tương ứng với dòng thống kê (0 nếu không rõ).</param>
        public static StatAgg? From(IEnumerable<PlayerStat> stats, Func<PlayerStat, int>? teamMatches = null)
        {
            var list = stats.Where(s => s.Appearances > 0 || s.Goals > 0 || s.YellowCards > 0 || s.RedCards > 0).ToList();
            if (list.Count == 0) return null;
            return new StatAgg
            {
                Appearances = list.Sum(s => s.Appearances),
                Starts = list.Sum(s => s.Starts),
                Minutes = list.Sum(s => s.MinutesPlayed),
                Goals = list.Sum(s => s.Goals),
                PenaltyGoals = list.Sum(s => s.PenaltyGoals ?? 0),
                CleanSheets = list.Sum(s => s.CleanSheets),
                GoalsConceded = list.Sum(s => s.GoalsConceded),
                YellowCards = list.Sum(s => s.YellowCards),
                RedCards = list.Sum(s => s.RedCards),
                TeamMatches = teamMatches == null ? 0 : list.Where(s => s.Appearances > 0).Sum(teamMatches)
            };
        }
    }

    /// <summary>Một cầu thủ kèm số liệu đã cộng dồn theo phạm vi giải đấu đang xét.</summary>
    public class PlayerRow
    {
        public Player Player { get; init; } = null!;
        public StatAgg? Stat { get; init; }
    }

    /// <summary>Định nghĩa một chỉ số có thể dùng để phân tích / xếp hạng.</summary>
    public class MetricDef
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Group { get; init; } = "";
        public bool IsStat { get; init; }
        public bool HigherIsBetter { get; init; } = true;
        public string Format { get; init; } = "0";
        public Func<Player, StatAgg?, double?> Get { get; init; } = (_, _) => null;

        public double? Value(PlayerRow r) => Get(r.Player, r.Stat);
        public string Display(double? v) => v.HasValue ? v.Value.ToString(Format, Fmt.VI) : "–";
    }

    public static class Metrics
    {
        public const string GroupAttr = "Năng lực (ước tính)";
        public const string GroupMatch = "Thi đấu";
        public const string GroupProfile = "Hồ sơ";

        public static readonly List<MetricDef> All = new()
        {
            // ----- Chỉ số năng lực (thang 1–100) -----
            new() { Key = "pace", Name = "Tốc độ", Group = GroupAttr, Get = (p, _) => p.Pace },
            new() { Key = "shooting", Name = "Dứt điểm", Group = GroupAttr, Get = (p, _) => p.Shooting },
            new() { Key = "passing", Name = "Chuyền bóng", Group = GroupAttr, Get = (p, _) => p.Passing },
            new() { Key = "dribbling", Name = "Rê bóng", Group = GroupAttr, Get = (p, _) => p.Dribbling },
            new() { Key = "defending", Name = "Phòng ngự", Group = GroupAttr, Get = (p, _) => p.Defending },
            new() { Key = "physical", Name = "Thể lực", Group = GroupAttr, Get = (p, _) => p.Physical },
            new() { Key = "ovr", Name = "Chỉ số tổng (thang 100)", Group = GroupAttr, Get = (p, _) => p.Overall },
            // ----- Thống kê thi đấu (dữ liệu thật) -----
            new() { Key = "goals", Name = "Bàn thắng", Group = GroupMatch, IsStat = true, Get = (_, s) => s?.Goals },
            new() { Key = "goals90", Name = "Bàn thắng / 90'", Group = GroupMatch, IsStat = true, Format = "0.00", Get = (_, s) => s?.Per90(s.Goals) },
            new() { Key = "minutes", Name = "Số phút thi đấu", Group = GroupMatch, IsStat = true, Get = (_, s) => s == null || s.Minutes == 0 ? null : s.Minutes },
            new() { Key = "minshare", Name = "Tỉ lệ thời gian thi đấu (%)", Group = GroupMatch, IsStat = true, Format = "0.0", Get = (_, s) => s == null || s.TeamMatches == 0 ? null : Math.Min(100, s.Minutes * 100.0 / (s.TeamMatches * 90)) },
            new() { Key = "startrate", Name = "Tỉ lệ đá chính (%)", Group = GroupMatch, IsStat = true, Format = "0.0", Get = (_, s) => s == null || s.Appearances == 0 ? null : s.Starts * 100.0 / s.Appearances },
            new() { Key = "csrate", Name = "Tỉ lệ giữ sạch lưới (%)", Group = GroupMatch, IsStat = true, Format = "0.0", Get = (p, s) => p.Position != Position.GK || s == null || s.Appearances == 0 ? null : s.CleanSheets * 100.0 / s.Appearances },
            new() { Key = "gc90", Name = "Bàn thua / 90' (thủ môn, càng ít càng tốt)", Group = GroupMatch, IsStat = true, HigherIsBetter = false, Format = "0.00", Get = (p, s) => p.Position != Position.GK ? null : s?.Per90(s.GoalsConceded) },
            new() { Key = "cards90", Name = "Thẻ phạt / 90' (càng ít càng tốt)", Group = GroupMatch, IsStat = true, HigherIsBetter = false, Format = "0.00", Get = (_, s) => s?.Per90(s.YellowCards + 2 * s.RedCards) },
            // ----- Hồ sơ -----
            new() { Key = "height", Name = "Chiều cao (cm)", Group = GroupProfile, Get = (p, _) => p.HeightCm },
            new() { Key = "value", Name = "Giá trị ước tính (tỷ VNĐ)", Group = GroupProfile, Format = "0.0", Get = (p, _) => p.MarketValue / 1000.0 },
            new() { Key = "youth", Name = "Tuổi (càng trẻ càng tốt)", Group = GroupProfile, HigherIsBetter = false, Get = (p, _) => p.Age },
        };

        public static MetricDef? Find(string key) => All.FirstOrDefault(m => m.Key == key);

        /// <summary>Các chỉ số thi đấu tiêu biểu hiển thị cho phân tích theo nhóm vị trí.</summary>
        public static string[] KeyMetrics(PositionGroup g) => g switch
        {
            PositionGroup.GK => new[] { "csrate", "gc90", "minshare", "startrate", "cards90" },
            PositionGroup.DEF => new[] { "minshare", "startrate", "goals90", "cards90", "minutes" },
            PositionGroup.MID => new[] { "goals90", "minshare", "startrate", "cards90", "minutes" },
            _ => new[] { "goals90", "goals", "minshare", "startrate", "cards90" }
        };

        public static readonly string[] Attributes = { "pace", "shooting", "passing", "dribbling", "defending", "physical" };
    }

    public class RankedRow
    {
        public PlayerRow Row { get; init; } = null!;
        public double Score { get; init; }
        public Dictionary<string, double?> Values { get; init; } = new();
        public Dictionary<string, double> Normalized { get; init; } = new();
    }

    public interface IAnalysisService
    {
        Task<List<Tournament>> TournamentsWithStatsAsync();
        Task<int?> DefaultTournamentIdAsync();
        Task<List<PlayerRow>> LoadRowsAsync(int? tournamentId, PositionGroup? group = null, int? teamId = null,
            int? minAge = null, int? maxAge = null, IEnumerable<int>? playerIds = null, bool excludeRetired = true);
        List<RankedRow> Rank(IEnumerable<PlayerRow> pool, IDictionary<string, int> weights);
        Dictionary<string, double?> Percentiles(PlayerRow target, IEnumerable<PlayerRow> pool, IEnumerable<string> metricKeys);
        Task<Dictionary<(int TournamentId, int TeamId), int>> TeamMatchCountsAsync();
        Task<List<StandingRow>> StandingsAsync(int tournamentId);
        /// <summary>Diễn giải 6 chỉ số năng lực bằng số liệu thật và giá trị quy đổi có đơn vị.</summary>
        Task<List<AttrInsight>> InsightsAsync(Player p);
    }

    /// <summary>Một dòng bảng xếp hạng đội (tính từ kết quả các trận đã đá).</summary>
    public class StandingRow
    {
        public int Rank { get; set; }
        public Team Team { get; init; } = null!;
        public int Played { get; set; }
        public int Won { get; set; }
        public int Drawn { get; set; }
        public int Lost { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int GoalDiff => GoalsFor - GoalsAgainst;
        public int Points => Won * 3 + Drawn;
        /// <summary>Kết quả 5 trận gần nhất, cũ → mới: T / H / B.</summary>
        public List<char> Form { get; } = new();
    }

    /// <summary>Xử lý phân tích hiệu suất, chuẩn hóa và xếp hạng cầu thủ.</summary>
    public class AnalysisService : IAnalysisService
    {
        private readonly AppDbContext _db;
        private readonly IDataCache _cache;
        public AnalysisService(AppDbContext db, IDataCache? cache = null) { _db = db; _cache = cache ?? NoDataCache.Instance; }

        // Các truy vấn dưới đây dùng chung cho mọi người dùng → lưu bộ nhớ đệm (tự làm mới khi dữ liệu đổi).
        public Task<List<Tournament>> TournamentsWithStatsAsync()
            => _cache.GetAsync("tournaments-with-stats", () => _db.Tournaments.AsNoTracking()
                .Where(t => t.PlayerStats.Any())
                .OrderByDescending(t => t.Season).ThenBy(t => t.Type).ThenBy(t => t.Name)
                .ToListAsync());

        /// <summary>Mặc định: giải VĐQG mùa mới nhất có số liệu ra sân chi tiết.</summary>
        public Task<int?> DefaultTournamentIdAsync() => _cache.GetAsync("default-tournament", async () =>
        {
            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.PlayerStats.Any())
                .OrderBy(x => x.HasMatchDetails ? 0 : 1)
                .ThenBy(x => x.Type == TournamentType.League ? 0 : 1)
                .ThenByDescending(x => x.Season)
                .FirstOrDefaultAsync();
            return t?.Id;
        });

        /// <summary>Toàn bộ cầu thủ kèm đội + thống kê (bản dùng chung, chỉ đọc) – lọc trong bộ nhớ thay vì truy vấn lại CSDL mỗi lần.</summary>
        private Task<List<Player>> PlayerSnapshotAsync()
            => _cache.GetAsync("players-snapshot", () => _db.Players.AsNoTracking().Include(p => p.Team).Include(p => p.Stats).ToListAsync());

        /// <param name="tournamentId">Id giải đấu; 0 hoặc null = cộng dồn tất cả giải.</param>
        public async Task<List<PlayerRow>> LoadRowsAsync(int? tournamentId, PositionGroup? group = null, int? teamId = null,
            int? minAge = null, int? maxAge = null, IEnumerable<int>? playerIds = null, bool excludeRetired = true)
        {
            IEnumerable<Player> q = await PlayerSnapshotAsync();
            if (group.HasValue)
            {
                var members = PositionGroups.Members(group.Value);
                q = q.Where(p => members.Contains(p.Position));
            }
            if (teamId.HasValue) q = teamId == 0 ? q.Where(p => p.TeamId == null) : q.Where(p => p.TeamId == teamId);
            var today = DateTime.Today;
            if (minAge.HasValue) q = q.Where(p => p.DateOfBirth <= today.AddYears(-minAge.Value));
            if (maxAge.HasValue) q = q.Where(p => p.DateOfBirth > today.AddYears(-(maxAge.Value + 1)));
            if (playerIds != null)
            {
                var ids = playerIds.ToHashSet();
                q = q.Where(p => ids.Contains(p.Id));
            }
            if (excludeRetired) q = q.Where(p => p.Status != PlayerStatus.Retired);

            var players = q.ToList();
            var all = tournamentId is null or 0;
            var games = await TeamMatchCountsAsync();
            int TeamGames(PlayerStat s) => s.TeamId.HasValue ? games.GetValueOrDefault((s.TournamentId, s.TeamId.Value)) : 0;
            return players.Select(p => new PlayerRow
            {
                Player = p,
                Stat = StatAgg.From(all ? p.Stats : p.Stats.Where(s => s.TournamentId == tournamentId), TeamGames)
            }).ToList();
        }

        /// <summary>Số trận đã đá của từng đội trong từng giải có số liệu ra sân chi tiết.</summary>
        public Task<Dictionary<(int TournamentId, int TeamId), int>> TeamMatchCountsAsync() => _cache.GetAsync("team-match-counts", async () =>
        {
            var ms = await _db.Matches.AsNoTracking()
                .Where(m => m.HomeGoals != null && m.Tournament!.HasMatchDetails)
                .Select(m => new { m.TournamentId, m.HomeTeamId, m.AwayTeamId }).ToListAsync();
            return ms.SelectMany(m => new[] { (m.TournamentId, m.HomeTeamId), (m.TournamentId, m.AwayTeamId) })
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        });

        /// <summary>
        /// Xếp hạng: chuẩn hóa Min–Max từng tiêu chí trong nhóm ứng viên về [0,1]
        /// (đảo chiều với tiêu chí "càng ít càng tốt"), điểm = Σ wᵢ·xᵢ / Σ wᵢ × 100.
        /// Ứng viên thiếu số liệu của một tiêu chí thống kê được tính 0 ở tiêu chí đó.
        /// </summary>
        public List<RankedRow> Rank(IEnumerable<PlayerRow> pool, IDictionary<string, int> weights)
        {
            var rows = pool.ToList();
            var active = weights.Where(w => w.Value > 0 && Metrics.Find(w.Key) != null)
                .Select(w => (Def: Metrics.Find(w.Key)!, W: (double)w.Value)).ToList();
            var totalW = active.Sum(a => a.W);
            if (rows.Count == 0 || totalW <= 0) return new();

            var ranges = active.ToDictionary(a => a.Def.Key, a =>
            {
                var vals = rows.Select(r => a.Def.Value(r)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return vals.Count == 0 ? (Min: 0.0, Max: 0.0) : (Min: vals.Min(), Max: vals.Max());
            });

            return rows.Select(r =>
            {
                var values = new Dictionary<string, double?>();
                var norm = new Dictionary<string, double>();
                double score = 0;
                foreach (var (def, w) in active)
                {
                    var v = def.Value(r);
                    values[def.Key] = v;
                    var (min, max) = ranges[def.Key];
                    double x = !v.HasValue ? 0 : max - min < 1e-9 ? 1 : (v.Value - min) / (max - min);
                    if (!def.HigherIsBetter && v.HasValue) x = 1 - x;
                    norm[def.Key] = x;
                    score += w * x;
                }
                return new RankedRow { Row = r, Score = Math.Round(score / totalW * 100, 1), Values = values, Normalized = norm };
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Row.Player.Overall)
            .ToList();
        }

        public async Task<List<AttrInsight>> InsightsAsync(Player p)
        {
            var stats = await _db.PlayerStats.AsNoTracking().Include(s => s.Tournament).Include(s => s.Team)
                .Where(s => s.PlayerId == p.Id).ToListAsync();
            var det = stats.Where(s => s.Tournament!.HasMatchDetails && s.Appearances > 0)
                .OrderByDescending(s => s.Tournament!.Season).FirstOrDefault();
            var prev = stats.Where(s => !s.Tournament!.HasMatchDetails)
                .OrderByDescending(s => s.Tournament!.Season).FirstOrDefault();
            int teamMatches = 0; double? ga = null, gf = null;
            if (det?.TeamId != null)
            {
                var ms = await _db.Matches.AsNoTracking()
                    .Where(m => m.TournamentId == det.TournamentId && m.HomeGoals != null && (m.HomeTeamId == det.TeamId || m.AwayTeamId == det.TeamId))
                    .ToListAsync();
                teamMatches = ms.Count;
                if (ms.Count > 0)
                {
                    ga = ms.Average(m => (double)(m.HomeTeamId == det.TeamId ? m.AwayGoals!.Value : m.HomeGoals!.Value));
                    gf = ms.Average(m => (double)(m.HomeTeamId == det.TeamId ? m.HomeGoals!.Value : m.AwayGoals!.Value));
                }
            }
            var d = new InsightData
            {
                Apps = det?.Appearances ?? 0, Starts = det?.Starts ?? 0, Minutes = det?.MinutesPlayed ?? 0, Goals = det?.Goals ?? 0,
                CleanSheets = det?.CleanSheets ?? 0, GoalsConceded = det?.GoalsConceded ?? 0,
                YellowCards = det?.YellowCards ?? 0, RedCards = det?.RedCards ?? 0, TeamMatches = teamMatches,
                DetailSeason = det == null ? null : $"{det.Tournament!.Name} {det.Tournament.Season}",
                PrevGoals = prev?.Goals ?? 0, PrevPenalties = prev?.PenaltyGoals,
                PrevSeason = prev == null ? null : $"{prev.Tournament!.Name} {prev.Tournament.Season}",
                TeamConcededPerGame = ga, TeamScoredPerGame = gf, TeamName = det?.Team?.Name
            };
            return PlayerInsights.Build(p, d);
        }

        /// <summary>
        /// Bảng xếp hạng: 3 điểm/thắng, 1 điểm/hòa; xếp theo Điểm → Hiệu số → Bàn thắng → Tên đội.
        /// (Điều lệ V.League ưu tiên đối đầu trực tiếp khi bằng điểm; hệ thống dùng hiệu số cho đơn giản và ghi chú trên giao diện.)
        /// </summary>
        public Task<List<StandingRow>> StandingsAsync(int tournamentId) => _cache.GetAsync("standings:" + tournamentId, async () =>
        {
            var ms = await _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam)
                .Where(m => m.TournamentId == tournamentId && m.HomeGoals != null && m.AwayGoals != null)
                .OrderBy(m => m.KickOff).ToListAsync();
            var table = new Dictionary<int, StandingRow>();
            StandingRow Row(Team t) => table.TryGetValue(t.Id, out var r) ? r : table[t.Id] = new StandingRow { Team = t };
            foreach (var m in ms)
            {
                var h = Row(m.HomeTeam!); var a = Row(m.AwayTeam!);
                int hg = m.HomeGoals!.Value, ag = m.AwayGoals!.Value;
                h.Played++; a.Played++;
                h.GoalsFor += hg; h.GoalsAgainst += ag; a.GoalsFor += ag; a.GoalsAgainst += hg;
                if (hg > ag) { h.Won++; a.Lost++; h.Form.Add('T'); a.Form.Add('B'); }
                else if (hg < ag) { a.Won++; h.Lost++; a.Form.Add('T'); h.Form.Add('B'); }
                else { h.Drawn++; a.Drawn++; h.Form.Add('H'); a.Form.Add('H'); }
            }
            var list = table.Values
                .OrderByDescending(r => r.Points).ThenByDescending(r => r.GoalDiff).ThenByDescending(r => r.GoalsFor).ThenBy(r => r.Team.Name)
                .ToList();
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Rank = i + 1;
                if (list[i].Form.Count > 5) list[i].Form.RemoveRange(0, list[i].Form.Count - 5);
            }
            return list;
        });

        /// <summary>Phân vị (0–100): tỉ lệ cầu thủ trong nhóm so sánh có giá trị kém hơn hoặc bằng cầu thủ đang xét.</summary>
        public Dictionary<string, double?> Percentiles(PlayerRow target, IEnumerable<PlayerRow> pool, IEnumerable<string> metricKeys)
        {
            var list = pool.ToList();
            var result = new Dictionary<string, double?>();
            foreach (var key in metricKeys)
            {
                var def = Metrics.Find(key);
                var v = def?.Value(target);
                if (def == null || !v.HasValue) { result[key] = null; continue; }
                var vals = list.Select(def.Value).Where(x => x.HasValue).Select(x => x!.Value).ToList();
                if (vals.Count == 0) { result[key] = null; continue; }
                var worseOrEqual = def.HigherIsBetter ? vals.Count(x => x <= v.Value + 1e-9) : vals.Count(x => x >= v.Value - 1e-9);
                result[key] = Math.Round(worseOrEqual * 100.0 / vals.Count);
            }
            return result;
        }
    }
}
