using ScoutVN.Services;

namespace ScoutVN.Models.ViewModels
{
    /// <summary>"Điểm nhấn số liệu" tự sinh từ dữ liệu thống kê.</summary>
    public record Highlight(string Icon, string Label, string Title, string Value, string Sub, string? Url);

    public class TournamentCard
    {
        public Tournament Tournament { get; set; } = null!;
        public int Players { get; set; }
        public int Goals { get; set; }
        public string? TopScorer { get; set; }
        public int TopScorerGoals { get; set; }
        public int? TopScorerId { get; set; }
    }

    public class TeamCard
    {
        public Team Team { get; set; } = null!;
        public int Players { get; set; }
        public double AvgOverall { get; set; }
        public int Goals { get; set; }
    }

    public class LeaderRow
    {
        public Player Player { get; set; } = null!;
        public int Appearances { get; set; }
        public int Minutes { get; set; }
        public string Value { get; set; } = "";
        public double Raw { get; set; }
    }

    public class Leaderboards
    {
        public List<LeaderRow> Goals { get; set; } = new();
        public List<LeaderRow> Minutes { get; set; } = new();
        public List<LeaderRow> Cards { get; set; } = new();
        public List<LeaderRow> CleanSheets { get; set; } = new();
    }

    public class PortalHomeViewModel
    {
        public int PlayerCount { get; set; }
        public int TeamCount { get; set; }
        public int TournamentCount { get; set; }
        public int SeasonGoals { get; set; }
        public Tournament? CurrentLeague { get; set; }
        public List<StandingRow> Standings { get; set; } = new();
        public List<News> Featured { get; set; } = new();
        public List<News> Latest { get; set; } = new();
        public List<Highlight> Highlights { get; set; } = new();
        public Leaderboards Leaders { get; set; } = new();
        public List<TournamentCard> Tournaments { get; set; } = new();
        public List<TeamCard> Teams { get; set; } = new();
    }

    public class PortalTournamentViewModel
    {
        public Tournament Tournament { get; set; } = null!;
        public int Players { get; set; }
        public int Goals { get; set; }
        public int MatchesPlayed { get; set; }
        public int YellowCards { get; set; }
        public Leaderboards Leaders { get; set; } = new();
        public List<ChartPoint> GoalsByTeam { get; set; } = new();
        public List<StandingRow> Standings { get; set; } = new();
        public List<Match> Matches { get; set; } = new();
        public List<News> RelatedNews { get; set; } = new();
    }

    public class PortalNewsListViewModel
    {
        public string? Keyword { get; set; }
        public NewsCategory? Category { get; set; }
        public PagedList<News> Items { get; set; } = null!;
        public List<News> MostRead { get; set; } = new();
        /// <summary>Số bài theo chuyên mục (theo từ khóa đang tìm) – hiện trên các nút lọc.</summary>
        public Dictionary<NewsCategory, int> Counts { get; set; } = new();
        public int Total => Counts.Values.Sum();
    }

    public class PortalNewsDetailViewModel
    {
        public News News { get; set; } = null!;
        public List<News> Related { get; set; } = new();
    }

    public class PortalTeamViewModel
    {
        public Team Team { get; set; } = null!;
        public Tournament? League { get; set; }
        public List<PlayerRow> Squad { get; set; } = new();
        /// <summary>Định giá câu lạc bộ (tổng giá trị ước tính đội hình).</summary>
        public ClubValue? Value { get; set; }
    }
}
