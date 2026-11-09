using System.Globalization;
using ScoutVN.Models;

namespace ScoutVN.Services
{
    /// <summary>
    /// Một nhu cầu chuyên môn của HLV ("cần cầu thủ tì đè tốt", "cần thủ môn giữ sạch lưới"...).
    /// Mỗi nhu cầu được ánh xạ sang một hoặc nhiều chỉ số (Metrics) kèm trọng số 1–3.
    /// </summary>
    public class CoachNeed
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Icon { get; init; } = "bi-check2";
        public string Hint { get; init; } = "";
        public Dictionary<string, int> Weights { get; init; } = new();
        /// <summary>Chỉ hiện khi chọn nhóm Thủ môn.</summary>
        public bool GkOnly { get; init; }
        /// <summary>Không áp dụng cho thủ môn.</summary>
        public bool OutfieldOnly { get; init; }
    }

    /// <summary>Bộ lọc cứng theo yêu cầu của HLV (đơn vị thực: cm, km/h, tỷ VNĐ...).</summary>
    public class NeedFilter
    {
        public List<string> Needs { get; set; } = new();
        public int? MinHeight { get; set; }
        /// <summary>Tốc độ nước rút tối thiểu (km/h, quy đổi từ điểm tốc độ).</summary>
        public double? MinSpeed { get; set; }
        /// <summary>"Left" / "Right" / null.</summary>
        public string? Foot { get; set; }
        /// <summary>"vn" = cầu thủ Việt Nam, "foreign" = ngoại binh, null = tất cả.</summary>
        public string? Origin { get; set; }
        /// <summary>Giá trị ước tính tối đa (tỷ VNĐ).</summary>
        public double? MaxValue { get; set; }
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }

        public IEnumerable<PlayerRow> Apply(IEnumerable<PlayerRow> rows)
        {
            foreach (var r in rows)
            {
                var p = r.Player;
                if (MinHeight.HasValue && (p.HeightCm ?? 0) < MinHeight) continue;
                if (MinSpeed.HasValue && PlayerInsights.PaceKmh(p.Pace) < MinSpeed - 1e-9) continue;
                if (Foot == "Left" && p.PreferredFoot is not (PreferredFoot.Left or PreferredFoot.Both)) continue;
                if (Foot == "Right" && p.PreferredFoot is not (PreferredFoot.Right or PreferredFoot.Both)) continue;
                if (Origin == "vn" && p.Nationality != "Việt Nam") continue;
                if (Origin == "foreign" && p.Nationality == "Việt Nam") continue;
                if (MaxValue.HasValue && (p.MarketValue ?? 0) > MaxValue * 1000) continue;
                if (MinAge.HasValue && p.Age < MinAge) continue;
                if (MaxAge.HasValue && p.Age > MaxAge) continue;
                yield return r;
            }
        }

        /// <summary>Mô tả ngắn các điều kiện lọc (hiển thị phía trên kết quả).</summary>
        public List<string> Describe()
        {
            var vi = Fmt.VI;
            var l = new List<string>();
            if (MinHeight.HasValue) l.Add($"cao ≥ {MinHeight} cm");
            if (MinSpeed.HasValue) l.Add($"tốc độ ≥ {MinSpeed.Value.ToString("0.#", vi)} km/h");
            if (Foot == "Left") l.Add("thuận chân trái");
            if (Foot == "Right") l.Add("thuận chân phải");
            if (Origin == "vn") l.Add("cầu thủ Việt Nam");
            if (Origin == "foreign") l.Add("ngoại binh");
            if (MaxValue.HasValue) l.Add($"giá trị ≤ {MaxValue.Value.ToString("0.#", vi)} tỷ");
            if (MinAge.HasValue && MaxAge.HasValue) l.Add($"{MinAge}–{MaxAge} tuổi");
            else if (MaxAge.HasValue) l.Add($"≤ {MaxAge} tuổi");
            else if (MinAge.HasValue) l.Add($"≥ {MinAge} tuổi");
            return l;
        }
    }

    /// <summary>Một cột kết quả hiển thị bằng đơn vị thực.</summary>
    public class NeedColumn
    {
        public string Key { get; init; } = "";
        public string Header { get; init; } = "";
        public Func<PlayerRow, string> Show { get; init; } = _ => "–";
    }

    public static class CoachNeeds
    {
        public static readonly List<CoachNeed> All = new()
        {
            new() { Key = "speed", Name = "Tốc độ, bứt tốc", Icon = "bi-lightning-charge", Hint = "Chạy nước rút nhanh, bứt phá", Weights = new() { ["pace"] = 3 }, OutfieldOnly = true },
            new() { Key = "finishing", Name = "Dứt điểm, săn bàn", Icon = "bi-bullseye", Hint = "Ghi nhiều bàn / 90 phút", Weights = new() { ["goals90"] = 3, ["shooting"] = 2 }, OutfieldOnly = true },
            new() { Key = "passing", Name = "Chuyền bóng, kiến thiết", Icon = "bi-arrow-left-right", Hint = "Phân phối, phát động tấn công", Weights = new() { ["passing"] = 3 } },
            new() { Key = "dribbling", Name = "Rê dắt, qua người", Icon = "bi-shuffle", Hint = "Đi bóng, đột phá", Weights = new() { ["dribbling"] = 3 }, OutfieldOnly = true },
            new() { Key = "defending", Name = "Phòng ngự, đánh chặn", Icon = "bi-shield", Hint = "Tắc bóng, cắt bóng, đọc tình huống", Weights = new() { ["defending"] = 3 }, OutfieldOnly = true },
            new() { Key = "strength", Name = "Tì đè, tranh chấp tay đôi", Icon = "bi-person-arms-up", Hint = "Thể lực, sức mạnh va chạm", Weights = new() { ["physical"] = 3, ["height"] = 1 } },
            new() { Key = "aerial", Name = "Không chiến, chiều cao", Icon = "bi-arrows-vertical", Hint = "Tranh chấp bóng bổng", Weights = new() { ["height"] = 3, ["physical"] = 1 } },
            new() { Key = "stamina", Name = "Bền bỉ, ra sân thường xuyên", Icon = "bi-stopwatch", Hint = "Tỉ lệ thời gian thi đấu cao", Weights = new() { ["minshare"] = 3, ["physical"] = 1 } },
            new() { Key = "reflex", Name = "Phản xạ, cản phá", Icon = "bi-hand-index", Hint = "Ít bàn thua / 90 phút", Weights = new() { ["defending"] = 3, ["gc90"] = 2 }, GkOnly = true },
            new() { Key = "cleansheet", Name = "Giữ sạch lưới", Icon = "bi-shield-check", Hint = "Tỉ lệ trận không thủng lưới", Weights = new() { ["csrate"] = 3 }, GkOnly = true },
            new() { Key = "young", Name = "Trẻ, tiềm năng", Icon = "bi-stars", Hint = "Ưu tiên cầu thủ ít tuổi", Weights = new() { ["youth"] = 2 } },
            new() { Key = "discipline", Name = "Kỷ luật, ít thẻ phạt", Icon = "bi-card-text", Hint = "Ít thẻ vàng / đỏ", Weights = new() { ["cards90"] = 2 } },
        };

        public static CoachNeed? Find(string key) => All.FirstOrDefault(n => n.Key == key);

        /// <summary>Nhu cầu hiển thị theo nhóm vị trí: "Giữ sạch lưới", "Phản xạ" chỉ hiện khi chọn Thủ môn.</summary>
        public static List<CoachNeed> Visible(PositionGroup? g)
            => All.Where(n => g == PositionGroup.GK ? !n.OutfieldOnly : !n.GkOnly).ToList();

        public static List<string> Defaults(PositionGroup? g) => g switch
        {
            PositionGroup.GK => new() { "reflex", "cleansheet", "stamina" },
            PositionGroup.DEF => new() { "defending", "strength", "aerial" },
            PositionGroup.MID => new() { "passing", "dribbling", "stamina" },
            PositionGroup.ATT => new() { "finishing", "speed" },
            _ => new() { "finishing", "passing", "stamina" }
        };

        /// <summary>Các nhu cầu hợp lệ với nhóm vị trí (không thêm bộ mặc định).</summary>
        // Cụm từ tiếng Việt (không dấu) HLV hay dùng → nhu cầu chuyên môn. Dùng cho trợ lý AI ở chế độ tự động.
        private static readonly (string Key, string[] Words)[] Phrases =
        {
            ("speed", new[] { "toc do", "nhanh", "but toc", "chay nhanh", "nuoc rut" }),
            ("finishing", new[] { "dut diem", "sut", "chot ha", "ghi ban", "ban thang", "san ban", "ghi nhieu ban", "lam ban" }),
            ("passing", new[] { "chuyen bong", "chuyen ban", "phat dong", "kien thiet", "kien tao", "phan phoi", "nhan quan" }),
            ("dribbling", new[] { "re bong", "re dat", "ky thuat", "dot pha", "qua nguoi", "di bong", "kheo leo" }),
            ("defending", new[] { "phong ngu", "tac bong", "cat bong", "danh chan", "doc tinh huong", "doc canh", "keo bom" }),
            ("strength", new[] { "ti de", "tranh chap", "tay doi", "suc manh", "va cham", "khoe", "to khoe", "the chat", "cao to", "lam tuong", "giu bong" }),
            ("aerial", new[] { "khong chien", "chieu cao", "cao lon", "bong bong", "danh dau", "do cao", "nguoi cao" }),
            ("stamina", new[] { "the luc", "ben bi", "sung suc", "ra san thuong xuyen", "da chinh", "on dinh", "duoc tin dung", "thi dau nhieu", "nhieu phut", "phong do" }),
            ("reflex", new[] { "phan xa", "can pha", "bat bong" }),
            ("cleansheet", new[] { "giu sach luoi", "sach luoi", "it thung luoi" }),
            ("young", new[] { "tre", "tiem nang", "tre tuoi" }),
            ("discipline", new[] { "ky luat", "it the", "it bi the", "sach se" }),
        };

        /// <summary>
        /// Nhận ra TẤT CẢ nhu cầu chuyên môn trong câu hỏi, VD "tốc độ nhanh và tì đè tốt" → speed + strength.
        /// Trả về theo thứ tự xuất hiện trong câu.
        /// </summary>
        public static List<string> DetectFromText(string? question)
        {
            var s = " " + System.Text.RegularExpressions.Regex.Replace(TextHelper.RemoveDiacritics(question).ToLowerInvariant(), @"[^a-z0-9 ]+", " ") + " ";
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
            var hits = new List<(string Key, int Pos)>();
            foreach (var (key, words) in Phrases)
            {
                var pos = words.Select(w => s.IndexOf(" " + w + " ", StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                if (pos >= 0) hits.Add((key, pos));
            }
            return hits.OrderBy(h => h.Pos).Select(h => h.Key).ToList();
        }

        public static List<string> Valid(IEnumerable<string>? needs, PositionGroup? g)
        {
            var visible = Visible(g).Select(n => n.Key).ToHashSet();
            return (needs ?? Enumerable.Empty<string>()).Where(visible.Contains).Distinct().ToList();
        }

        /// <summary>Giữ các nhu cầu hợp lệ với nhóm vị trí; rỗng → bộ mặc định.</summary>
        public static List<string> Normalize(IEnumerable<string>? needs, PositionGroup? g)
        {
            var visible = Visible(g).Select(n => n.Key).ToHashSet();
            var list = (needs ?? Enumerable.Empty<string>()).Where(visible.Contains).Distinct().ToList();
            return list.Count > 0 ? list : Defaults(g);
        }

        /// <summary>Cộng dồn trọng số các chỉ số từ những nhu cầu được chọn (mỗi chỉ số tối đa 3).</summary>
        public static Dictionary<string, int> MetricWeights(IEnumerable<string> needs)
        {
            var w = new Dictionary<string, int>();
            foreach (var n in needs.Select(Find).Where(n => n != null))
                foreach (var (k, v) in n!.Weights)
                    w[k] = Math.Min(3, w.GetValueOrDefault(k) + v);
            return w;
        }

        private static string N(double v, string f) => v.ToString(f, Fmt.VI);

        /// <summary>Cột hiển thị (đơn vị thực) cho một chỉ số.</summary>
        public static NeedColumn? Column(string metric) => metric switch
        {
            "pace" => new() { Key = metric, Header = "Tốc độ", Show = r => $"≈ {N(PlayerInsights.PaceKmh(r.Player.Pace), "0.0")} km/h" },
            "shooting" => new() { Key = metric, Header = "Dứt điểm", Show = r => $"{r.Player.Shooting}/100" },
            "passing" => new() { Key = metric, Header = "Chuyền bóng", Show = r => $"{r.Player.Passing}/100" },
            "dribbling" => new() { Key = metric, Header = "Rê bóng", Show = r => $"{r.Player.Dribbling}/100" },
            "defending" => new() { Key = metric, Header = "Phòng ngự", Show = r => $"{r.Player.Defending}/100" },
            "physical" => new() { Key = metric, Header = "Thể lực, tì đè", Show = r => $"{r.Player.Physical}/100" },
            "goals90" => new() { Key = metric, Header = "Bàn thắng", Show = r => r.Stat == null ? "–" : r.Stat.Minutes > 0 ? $"{r.Stat.Goals} ({N(r.Stat.Per90(r.Stat.Goals) ?? 0, "0.00")}/90')" : $"{r.Stat.Goals}" },
            "height" => new() { Key = metric, Header = "Chiều cao", Show = r => r.Player.HeightCm.HasValue ? $"{r.Player.HeightCm} cm" : "–" },
            "minshare" => new() { Key = metric, Header = "Thời gian thi đấu", Show = r => r.Stat == null || r.Stat.Minutes == 0 ? "–" : Metrics.Find("minshare")!.Value(r) is double v ? $"{r.Stat.Minutes:N0}' ({N(v, "0")}%)" : $"{r.Stat.Minutes:N0}'" },
            "csrate" => new() { Key = metric, Header = "Giữ sạch lưới", Show = r => r.Stat == null || r.Stat.Appearances == 0 ? "–" : $"{r.Stat.CleanSheets}/{r.Stat.Appearances} trận" },
            "gc90" => new() { Key = metric, Header = "Bàn thua / 90'", Show = r => r.Stat?.Per90(r.Stat.GoalsConceded) is double v ? N(v, "0.00") : "–" },
            "youth" => new() { Key = metric, Header = "Tuổi", Show = r => r.Player.Age.ToString() },
            "cards90" => new() { Key = metric, Header = "Thẻ phạt", Show = r => r.Stat == null ? "–" : $"{r.Stat.YellowCards} vàng · {r.Stat.RedCards} đỏ" },
            "value" => new() { Key = metric, Header = "Giá trị", Show = r => Fmt.Money(r.Player.MarketValue, false) },
            _ => null
        };

        /// <summary>Các cột kết quả: chỉ số của nhu cầu được chọn + chiều cao + giá trị.</summary>
        public static List<NeedColumn> Columns(IEnumerable<string> needs)
        {
            var keys = MetricWeights(needs).OrderByDescending(kv => kv.Value).Select(kv => kv.Key)
                .Where(k => k != "youth").ToList();
            if (!keys.Contains("height")) keys.Add("height");
            keys.Add("value");
            return keys.Select(Column).Where(c => c != null).Select(c => c!).ToList();
        }
    }
}
