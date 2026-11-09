namespace ScoutVN.Models.ViewModels
{
    /// <summary>Bộ lọc tìm kiếm cầu thủ (đọc từ query string).</summary>
    public class PlayerFilter
    {
        public string? Keyword { get; set; }
        public int? TeamId { get; set; }
        public Position? Position { get; set; }
        public PreferredFoot? Foot { get; set; }
        public PlayerStatus? Status { get; set; }
        public string? Nationality { get; set; }
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int? MinHeight { get; set; }
        public string? Sort { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
    }

    public class PlayerIndexViewModel
    {
        public PlayerFilter Filter { get; set; } = new();
        public PagedList<Player> Players { get; set; } = null!;
    }

    public class TeamListItem
    {
        public Team Team { get; set; } = null!;
        public int PlayerCount { get; set; }
        public double AverageOverall { get; set; }
        public int ForeignCount { get; set; }
    }

    public class TeamIndexViewModel
    {
        public string? Keyword { get; set; }
        public string? City { get; set; }
        public string? Sort { get; set; }
        public List<string> Cities { get; set; } = new();
        public PagedList<TeamListItem> Teams { get; set; } = null!;
    }

    public class TournamentListItem
    {
        public Tournament Tournament { get; set; } = null!;
        public int PlayerCount { get; set; }
        public int TotalGoals { get; set; }
    }

    public class TournamentIndexViewModel
    {
        public string? Keyword { get; set; }
        public string? Season { get; set; }
        public TournamentType? Type { get; set; }
        public List<string> Seasons { get; set; } = new();
        public PagedList<TournamentListItem> Tournaments { get; set; } = null!;
    }

    public class PlayerStatIndexViewModel
    {
        public string? Keyword { get; set; }
        public int? TournamentId { get; set; }
        public int? TeamId { get; set; }
        public string? Sort { get; set; }
        public PagedList<PlayerStat> Stats { get; set; } = null!;
    }

    public class DashboardViewModel
    {
        public int PlayerCount { get; set; }
        public int TeamCount { get; set; }
        public int TournamentCount { get; set; }
        public int StatCount { get; set; }
        public int UserCount { get; set; }
        public List<Player> TopPlayers { get; set; } = new();
        public List<PlayerStat> TopScorers { get; set; } = new();
        public List<(string Label, int Count)> PositionGroups { get; set; } = new();
        public List<Player> RecentlyUpdated { get; set; } = new();
    }
}
