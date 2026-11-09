using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;

namespace ScoutVN.Services
{
    /// <summary>Định giá câu lạc bộ = tổng giá trị ước tính của các cầu thủ đang thuộc đội (đơn vị lưu: triệu VNĐ).</summary>
    public class ClubValue
    {
        public int TeamId { get; init; }
        public long Total { get; init; }
        public int Players { get; init; }
        /// <summary>Số cầu thủ có giá trị ước tính.</summary>
        public int Valued { get; init; }
        public double Average => Valued == 0 ? 0 : (double)Total / Valued;
        public int Rank { get; set; }
        public int TeamCount { get; set; }
        /// <summary>Trung bình tổng giá trị của các đội trong giải.</summary>
        public double LeagueAverage { get; set; }
        public long Foreign { get; init; }
        public List<(string Name, long Value)> ByLine { get; init; } = new();
        public List<Player> MostValuable { get; init; } = new();
    }

    public interface IClubValuationService
    {
        Task<ClubValue?> ForTeamAsync(int teamId);
        Task<Dictionary<int, ClubValue>> AllAsync();
    }

    public class ClubValuationService : IClubValuationService
    {
        private readonly AppDbContext _db;
        private readonly IDataCache _cache;
        public ClubValuationService(AppDbContext db, IDataCache cache) { _db = db; _cache = cache; }

        public async Task<ClubValue?> ForTeamAsync(int teamId)
            => (await AllAsync()).GetValueOrDefault(teamId);

        // Dùng chung cho mọi người xem (bộ nhớ đệm, tự làm mới khi dữ liệu cầu thủ thay đổi)
        public Task<Dictionary<int, ClubValue>> AllAsync() => _cache.GetAsync("club-values", ComputeAsync);

        private async Task<Dictionary<int, ClubValue>> ComputeAsync()
        {
            // Chỉ tính cầu thủ đang thuộc biên chế (bỏ cầu thủ đã rời giải / giải nghệ)
            var players = await _db.Players.AsNoTracking()
                .Where(p => p.TeamId != null && p.Status != PlayerStatus.LeftLeague && p.Status != PlayerStatus.Retired)
                .ToListAsync();
            var list = players.GroupBy(p => p.TeamId!.Value).Select(g => new ClubValue
            {
                TeamId = g.Key,
                Total = g.Sum(p => (long)(p.MarketValue ?? 0)),
                Players = g.Count(),
                Valued = g.Count(p => p.MarketValue > 0),
                Foreign = g.Where(p => p.Nationality != "Việt Nam").Sum(p => (long)(p.MarketValue ?? 0)),
                ByLine = Enum.GetValues<PositionGroup>()
                    .Select(gr => (PositionGroups.Name(gr), g.Where(p => PositionGroups.Of(p.Position) == gr).Sum(p => (long)(p.MarketValue ?? 0))))
                    .ToList(),
                MostValuable = g.Where(p => p.MarketValue > 0).OrderByDescending(p => p.MarketValue).Take(3).ToList()
            }).OrderByDescending(v => v.Total).ToList();
            var avg = list.Count == 0 ? 0 : list.Average(v => (double)v.Total);
            for (int i = 0; i < list.Count; i++) { list[i].Rank = i + 1; list[i].TeamCount = list.Count; list[i].LeagueAverage = avg; }
            return list.ToDictionary(v => v.TeamId);
        }
    }
}
