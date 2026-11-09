namespace ScoutVN.Services
{
    /// <summary>Một chiều của vector đặc trưng dùng cho KNN.</summary>
    public class KnnFeature
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        /// <summary>Bước nhập trên giao diện (chỉ số năng lực 1, số liệu /90' 0.05 ...).</summary>
        public double Step { get; init; } = 1;
        public MetricDef Metric => Metrics.Find(Key)!;
    }

    /// <summary>Yêu cầu gợi ý: vector mục tiêu + trọng số + phạm vi ứng viên.</summary>
    public class KnnQuery
    {
        public PositionGroup? Group { get; set; }
        public int? TournamentId { get; set; }
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int MinMinutes { get; set; } = DefaultMinMinutes;
        /// <summary>Ngưỡng phút tối thiểu mặc định (mùa 2026/27 mới qua 6 vòng ≈ 540 phút tối đa).</summary>
        public const int DefaultMinMinutes = 180;
        public int K { get; set; } = 10;
        /// <summary>Giá trị mong muốn theo từng chiều (thang gốc, chưa chuẩn hóa).</summary>
        public Dictionary<string, double> Target { get; set; } = new();
        /// <summary>Trọng số từng chiều (0 = bỏ qua).</summary>
        public Dictionary<string, double> Weights { get; set; } = new();
        /// <summary>Cầu thủ mẫu (chế độ "tìm cầu thủ tương tự") – bị loại khỏi kết quả.</summary>
        public int? ExcludePlayerId { get; set; }
        /// <summary>Bộ lọc cứng theo nhu cầu HLV (chiều cao, tốc độ, chân thuận, nội/ngoại binh, giá trị).</summary>
        public NeedFilter? Filter { get; set; }
    }

    public class KnnNeighbor
    {
        public PlayerRow Row { get; init; } = null!;
        public double Distance { get; init; }
        /// <summary>Mức độ phù hợp (%) = 100·(1 − d), d ∈ [0,1].</summary>
        public double Match { get; init; }
        public Dictionary<string, double> Values { get; init; } = new();
        /// <summary>Độ lệch chuẩn hóa (giá trị ứng viên − mục tiêu) theo từng chiều.</summary>
        public Dictionary<string, double> Diff { get; init; } = new();
        /// <summary>Giá trị đã chuẩn hóa Min–Max [0,1] theo từng chiều đang dùng.</summary>
        public Dictionary<string, double> Normalized { get; init; } = new();
        /// <summary>Các chiều gần mục tiêu nhất (giải thích kết quả).</summary>
        public List<string> Strengths { get; init; } = new();
        /// <summary>Các chiều thấp hơn mục tiêu nhiều nhất.</summary>
        public List<string> Gaps { get; init; } = new();
    }

    public class KnnResult
    {
        public List<KnnNeighbor> Neighbors { get; set; } = new();
        public int PoolSize { get; set; }
        public int ExcludedForMinutes { get; set; }
        public int ExcludedByFilter { get; set; }
        /// <summary>Vector mục tiêu đã chuẩn hóa [0,1] (các chiều có trọng số &gt; 0).</summary>
        public Dictionary<string, double> TargetNormalized { get; set; } = new();
    }

    public interface IKnnService
    {
        IReadOnlyList<KnnFeature> Features { get; }
        Task<KnnResult> RecommendAsync(KnnQuery q);
        /// <summary>Mức mong muốn mặc định: trung bình nhóm 10% ứng viên tốt nhất theo từng chiều.</summary>
        Task<Dictionary<string, double>> SuggestTargetAsync(PositionGroup? group, int? tournamentId, int minMinutes = KnnQuery.DefaultMinMinutes);
        Dictionary<string, double> VectorOf(PlayerRow row);
    }

    /// <summary>
    /// Gợi ý cầu thủ bằng K láng giềng gần nhất (KNN):
    /// vector 10 chiều → chuẩn hóa Min–Max theo nhóm ứng viên → khoảng cách Euclid có trọng số → Top-K.
    /// </summary>
    public class KnnService : IKnnService
    {
        public static readonly List<KnnFeature> FeatureList = new()
        {
            new() { Key = "pace", Name = "Tốc độ" },
            new() { Key = "shooting", Name = "Dứt điểm" },
            new() { Key = "passing", Name = "Chuyền bóng" },
            new() { Key = "dribbling", Name = "Rê bóng" },
            new() { Key = "defending", Name = "Phòng ngự" },
            new() { Key = "physical", Name = "Thể lực, tì đè" },
            new() { Key = "goals90", Name = "Ghi bàn", Step = 0.05 },
            new() { Key = "minshare", Name = "Thời gian thi đấu" },
            new() { Key = "csrate", Name = "Giữ sạch lưới" },
            new() { Key = "height", Name = "Chiều cao" },
        };

        /// <summary>Trọng số KNN (0–3) suy ra từ các nhu cầu của HLV (chỉ giữ các chiều có trong vector).</summary>
        public static Dictionary<string, double> WeightsFromNeeds(IEnumerable<string> needs)
        {
            var mw = CoachNeeds.MetricWeights(needs);
            return FeatureList.ToDictionary(f => f.Key, f => (double)mw.GetValueOrDefault(f.Key));
        }

        /// <summary>Trọng số mặc định theo nhóm vị trí (thang 0–3).</summary>
        public static Dictionary<string, double> DefaultWeights(PositionGroup? g) => g switch
        {
            PositionGroup.GK => new() { ["pace"] = 0, ["shooting"] = 0, ["passing"] = 1, ["dribbling"] = 0, ["defending"] = 3, ["physical"] = 2, ["goals90"] = 0, ["minshare"] = 2, ["csrate"] = 3 },
            PositionGroup.DEF => new() { ["pace"] = 2, ["shooting"] = 0, ["passing"] = 1, ["dribbling"] = 1, ["defending"] = 3, ["physical"] = 2, ["goals90"] = 0, ["minshare"] = 2, ["csrate"] = 0 },
            PositionGroup.MID => new() { ["pace"] = 1, ["shooting"] = 1, ["passing"] = 3, ["dribbling"] = 2, ["defending"] = 1, ["physical"] = 1, ["goals90"] = 1, ["minshare"] = 2, ["csrate"] = 0 },
            PositionGroup.ATT => new() { ["pace"] = 2, ["shooting"] = 3, ["passing"] = 1, ["dribbling"] = 2, ["defending"] = 0, ["physical"] = 1, ["goals90"] = 3, ["minshare"] = 2, ["csrate"] = 0 },
            _ => FeatureList.ToDictionary(f => f.Key, f => f.Key is "csrate" or "height" ? 0.0 : 1.0)
        };

        private readonly IAnalysisService _analysis;
        public KnnService(IAnalysisService analysis) => _analysis = analysis;

        public IReadOnlyList<KnnFeature> Features => FeatureList;

        public Dictionary<string, double> VectorOf(PlayerRow row)
            => FeatureList.ToDictionary(f => f.Key, f => f.Metric.Value(row) ?? 0);

        private async Task<(List<PlayerRow> Pool, int Excluded)> PoolAsync(PositionGroup? group, int? tid, int? minAge, int? maxAge, int minMinutes)
        {
            var rows = await _analysis.LoadRowsAsync(tid, group, null, minAge, maxAge);
            // Giải chỉ có bàn thắng / thẻ phạt (không có số phút) thì không lọc theo phút
            var hasMinutes = rows.Any(r => r.Stat?.Minutes > 0);
            var pool = rows.Where(r => r.Stat != null && (!hasMinutes || r.Stat.Minutes >= minMinutes)).ToList();
            return (pool, rows.Count - pool.Count);
        }

        public async Task<Dictionary<string, double>> SuggestTargetAsync(PositionGroup? group, int? tournamentId, int minMinutes = KnnQuery.DefaultMinMinutes)
        {
            var (pool, _) = await PoolAsync(group, tournamentId, null, null, minMinutes);
            var result = new Dictionary<string, double>();
            foreach (var f in FeatureList)
            {
                var vals = pool.Select(r => f.Metric.Value(r) ?? 0).OrderByDescending(v => v).ToList();
                var n = Math.Max(1, (int)Math.Ceiling(vals.Count * 0.1));
                var v = vals.Count == 0 ? 0 : vals.Take(n).Average();
                result[f.Key] = f.Step < 1 ? Math.Round(v, 2) : Math.Round(v);
            }
            return result;
        }

        public async Task<KnnResult> RecommendAsync(KnnQuery q)
        {
            var (pool, excluded) = await PoolAsync(q.Group, q.TournamentId, q.MinAge, q.MaxAge, q.MinMinutes);
            if (q.ExcludePlayerId.HasValue) pool = pool.Where(r => r.Player.Id != q.ExcludePlayerId).ToList();
            var beforeFilter = pool.Count;
            if (q.Filter != null) pool = q.Filter.Apply(pool).ToList();
            var res = new KnnResult { PoolSize = pool.Count, ExcludedForMinutes = excluded, ExcludedByFilter = beforeFilter - pool.Count };

            var active = FeatureList.Where(f => q.Weights.GetValueOrDefault(f.Key) > 0).ToList();
            var sumW = active.Sum(f => q.Weights[f.Key]);
            if (pool.Count == 0 || sumW <= 0) return res;

            // 1) Vector đặc trưng + khoảng giá trị (Min–Max) của nhóm ứng viên
            var vectors = pool.Select(r => (Row: r, V: VectorOf(r))).ToList();
            // Thiếu chiều cao (nguồn chưa có) → dùng trung vị của nhóm ứng viên để không làm lệch Min–Max
            var hs = vectors.Select(x => x.V["height"]).Where(h => h > 0).OrderBy(h => h).ToList();
            if (hs.Count > 0) foreach (var x in vectors) if (x.V["height"] <= 0) x.V["height"] = hs[hs.Count / 2];
            var range = active.ToDictionary(f => f.Key, f => (Min: vectors.Min(x => x.V[f.Key]), Max: vectors.Max(x => x.V[f.Key])));
            double Norm(string key, double v)
            {
                var (min, max) = range[key];
                if (max - min < 1e-9) return 1;
                return Math.Clamp((v - min) / (max - min), 0, 1);
            }
            var t = active.ToDictionary(f => f.Key, f => Norm(f.Key, q.Target.GetValueOrDefault(f.Key)));
            res.TargetNormalized = t;

            // 2) Khoảng cách Euclid có trọng số, quy về [0,1]: d = sqrt(Σ wᵢ(xᵢ − tᵢ)² / Σ wᵢ)
            var scored = vectors.Select(x =>
            {
                var diff = active.ToDictionary(f => f.Key, f => Norm(f.Key, x.V[f.Key]) - t[f.Key]);
                var d = Math.Sqrt(active.Sum(f => q.Weights[f.Key] * diff[f.Key] * diff[f.Key]) / sumW);
                return (x.Row, x.V, diff, d);
            });

            // 3) Lấy K láng giềng gần nhất
            res.Neighbors = scored
                .OrderBy(s => s.d).ThenByDescending(s => s.Row.Player.Overall)
                .Take(Math.Clamp(q.K, 1, 50))
                .Select(s => new KnnNeighbor
                {
                    Row = s.Row,
                    Distance = Math.Round(s.d, 4),
                    Match = Math.Round(100 * (1 - s.d), 1),
                    Values = s.V,
                    Diff = s.diff,
                    Normalized = s.diff.ToDictionary(kv => kv.Key, kv => kv.Value + t[kv.Key]),
                    // Điểm tương đồng: chiều quan trọng (trọng số lớn) mà ứng viên đạt hoặc gần đạt mục tiêu
                    Strengths = active.Where(f => s.diff[f.Key] > -0.1)
                        .OrderByDescending(f => q.Weights[f.Key]).ThenBy(f => Math.Abs(s.diff[f.Key]))
                        .Take(2).Select(f => f.Name).ToList(),
                    Gaps = active.Where(f => s.diff[f.Key] < -0.15)
                        .OrderBy(f => s.diff[f.Key] * q.Weights[f.Key]).Take(2).Select(f => f.Name).ToList()
                }).ToList();
            return res;
        }
    }
}
