using Microsoft.AspNetCore.Mvc.Rendering;
using ScoutVN.Services;

namespace ScoutVN.Models.ViewModels
{
    /// <summary>Một cặp nhãn – giá trị dùng cho biểu đồ.</summary>
    public record ChartPoint(string Label, double Value, int? Id = null);

    public class AnalysisOverviewViewModel
    {
        public int TournamentId { get; set; }
        public string ScopeName { get; set; } = "";
        public List<SelectListItem> Tournaments { get; set; } = new();
        /// <summary>Phạm vi có số liệu ra sân (phút) hay chỉ có bàn thắng / thẻ phạt.</summary>
        public bool HasMinutes { get; set; }
        public int PlayersWithStats { get; set; }
        public int MatchesPlayed { get; set; }
        public int TotalGoals { get; set; }
        public double HomeWinPct { get; set; }
        public double DrawPct { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public double GoalsPerMatch => MatchesPlayed > 0 ? (double)TotalGoals / MatchesPlayed : 0;
        public List<ChartPoint> TopScorers { get; set; } = new();
        public string SecondTitle { get; set; } = "";
        public string SecondLabel { get; set; } = "";
        public List<ChartPoint> SecondBoard { get; set; } = new();
        public List<ChartPoint> GoalsByTeam { get; set; } = new();
        public List<ChartPoint> GoalsByGroup { get; set; } = new();
        public List<StandingRow> Standings { get; set; } = new();
        /// <summary>Định giá câu lạc bộ (tỷ VNĐ), Id = đội.</summary>
        public List<ChartPoint> ClubValues { get; set; } = new();
        public List<ChartPoint> AgeGroups { get; set; } = new();
        /// <summary>Cầu thủ nổi bật theo từng nhóm vị trí (điểm tổng hợp cao nhất theo bộ tiêu chí mặc định).</summary>
        public List<(string Group, PlayerRow Row, double Score, string Criteria)> BestByGroup { get; set; } = new();
    }

    public class PercentileItem
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Display { get; set; } = "";
        public double? Percentile { get; set; }
    }

    public class SeasonPoint
    {
        public string Tournament { get; set; } = "";
        public bool HasDetails { get; set; }
        public string? Team { get; set; }
        public int Appearances { get; set; }
        public int Starts { get; set; }
        public int Goals { get; set; }
        public int? PenaltyGoals { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public int Minutes { get; set; }
    }

    public class PlayerAnalysisViewModel
    {
        public Player Player { get; set; } = null!;
        public PlayerRow Row { get; set; } = null!;
        public PositionGroup Group { get; set; }
        public int TournamentId { get; set; }
        public string ScopeName { get; set; } = "";
        public List<SelectListItem> Tournaments { get; set; } = new();
        public int[] Attributes { get; set; } = Array.Empty<int>();
        public double[] GroupAverage { get; set; } = Array.Empty<double>();
        public int PoolSize { get; set; }
        public int MinMinutes { get; set; }
        public List<PercentileItem> Percentiles { get; set; } = new();
        public List<SeasonPoint> Seasons { get; set; } = new();
        public List<AttrInsight> Insights { get; set; } = new();
        public List<string> Strengths { get; set; } = new();
        public List<string> Weaknesses { get; set; } = new();
    }

    public class CompareViewModel
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
        public int TournamentId { get; set; }
        public string ScopeName { get; set; } = "";
        public List<SelectListItem> Tournaments { get; set; } = new();
        /// <summary>Danh sách chọn cầu thủ, nhóm theo đội.</summary>
        public List<SelectListItem> PlayerOptions { get; set; } = new();
        /// <summary>Danh sách cho ô nhập tìm cầu thủ.</summary>
        public List<PlayerPick> Picks { get; set; } = new();
        public Dictionary<int, string> Names { get; set; } = new();
        public List<PlayerRow> Rows { get; set; } = new();
        public List<string> StatMetricKeys { get; set; } = new();
        public string? Message { get; set; }
    }

    public class RankingViewModel
    {
        public PositionGroup? Group { get; set; }
        public int TournamentId { get; set; }
        public int? TeamId { get; set; }
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int MinMinutes { get; set; } = KnnQuery.DefaultMinMinutes;
        public int Top { get; set; } = 20;
        public Dictionary<string, int> Weights { get; set; } = new();
        public List<SelectListItem> Tournaments { get; set; } = new();
        public List<SelectListItem> Teams { get; set; } = new();
        public List<RankedRow> Results { get; set; } = new();
        public List<MetricDef> ActiveMetrics { get; set; } = new();
        public int PoolSize { get; set; }
        public int ExcludedForMinutes { get; set; }
        public int ExcludedByFilter { get; set; }
        public string ScopeName { get; set; } = "";
        public List<string> Needs { get; set; } = new();
        /// <summary>Người dùng bấm xếp hạng nhưng chưa chọn nhu cầu (tiêu chí) nào.</summary>
        public bool NeedsMissing { get; set; }
        public NeedFilter Filter { get; set; } = new();
        public List<NeedColumn> Columns { get; set; } = new();
    }
}
