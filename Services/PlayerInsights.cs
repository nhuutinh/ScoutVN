using ScoutVN.Models;

namespace ScoutVN.Services
{
    /// <summary>
    /// Diễn giải một chỉ số năng lực bằng con số cụ thể: điểm (thang 100), mức đánh giá,
    /// giá trị quy đổi có đơn vị và các số liệu thật làm căn cứ.
    /// </summary>
    public class AttrInsight
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Icon { get; init; } = "bi-circle";
        public int Score { get; init; }
        public string Level => PlayerInsights.Level(Score);
        /// <summary>Con số chính có đơn vị, ví dụ “≈ 31,6 km/h” hoặc “0,57 bàn / 90 phút”.</summary>
        public string Measure { get; init; } = "";
        /// <summary>Giải thích ý nghĩa của con số chính.</summary>
        public string MeasureNote { get; init; } = "";
        /// <summary>true: con số chính là số liệu thật; false: quy đổi / ước tính.</summary>
        public bool MeasureIsReal { get; init; }
        public List<string> Evidence { get; init; } = new();
    }

    /// <summary>Số liệu thật dùng để diễn giải chỉ số (tổng hợp từ thống kê và trận đấu).</summary>
    public class InsightData
    {
        public int Apps { get; init; }
        public int Starts { get; init; }
        public int Minutes { get; init; }
        public int Goals { get; init; }
        public int CleanSheets { get; init; }
        public int GoalsConceded { get; init; }
        public int YellowCards { get; init; }
        public int RedCards { get; init; }
        public int TeamMatches { get; init; }
        public string? DetailSeason { get; init; }
        public int PrevGoals { get; init; }
        public int? PrevPenalties { get; init; }
        public string? PrevSeason { get; init; }
        /// <summary>Bàn thua trung bình mỗi trận của đội cầu thủ trong giải có số liệu chi tiết.</summary>
        public double? TeamConcededPerGame { get; init; }
        public double? TeamScoredPerGame { get; init; }
        public string? TeamName { get; init; }
    }

    public static class PlayerInsights
    {
        public static string Level(int v) => v switch { >= 80 => "Rất tốt", >= 70 => "Tốt", >= 60 => "Khá", >= 50 => "Trung bình", _ => "Hạn chế" };

        /// <summary>
        /// Quy đổi điểm tốc độ sang tốc độ chạy nước rút tối đa (km/h): 24 + 0,12 × điểm
        /// (điểm 50 ≈ 30 km/h, 90 ≈ 34,8 km/h – khoảng tốc độ thường gặp của cầu thủ chuyên nghiệp).
        /// Đây là giá trị quy đổi, KHÔNG phải số đo GPS.
        /// </summary>
        public static double PaceKmh(int pace) => Math.Round(24 + 0.12 * pace, 1);

        /// <summary>Thời gian chạy 30 m ước tính (giây) tương ứng với điểm tốc độ: 4,9 − 0,012 × điểm.</summary>
        public static double Sprint30m(int pace) => Math.Round(4.9 - 0.012 * pace, 2);

        private static string N(double v, string f = "0.00") => v.ToString(f, System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));

        public static List<AttrInsight> Build(Player p, InsightData d)
        {
            var gk = p.Position == Position.GK;
            var list = new List<AttrInsight>();
            double? per90(int v) => d.Minutes > 0 ? v * 90.0 / d.Minutes : null;
            var season = d.DetailSeason ?? "mùa hiện tại";

            // 1) Tốc độ
            var pace = new AttrInsight
            {
                Key = "pace", Name = "Tốc độ", Icon = "bi-lightning-charge", Score = p.Pace,
                Measure = $"≈ {N(PaceKmh(p.Pace), "0.0")} km/h", MeasureIsReal = false,
                MeasureNote = $"Tốc độ nước rút tối đa · chạy 30 m ≈ {N(Sprint30m(p.Pace))} giây",
            };
            pace.Evidence.Add($"Tuổi {p.Age}: " + (p.Age <= 22 ? "cầu thủ trẻ được cộng điểm tốc độ" : p.Age > 29 ? $"trên 29 tuổi bị trừ {N((p.Age - 29) * 1.5, "0.#")} điểm" : "độ tuổi đỉnh cao, không điều chỉnh"));
            pace.Evidence.Add($"Vị trí {p.Position}: mẫu tốc độ của vị trí là {Template(p.Position)[0]} điểm");
            list.Add(pace);

            // 2) Dứt điểm
            var g90 = per90(d.Goals);
            if (gk)
            {
                var gks = new AttrInsight
                {
                    Key = "shooting", Name = "Dứt điểm", Icon = "bi-bullseye", Score = p.Shooting, Measure = $"{p.Shooting}/100",
                    MeasureNote = "Thủ môn: không tính vào chỉ số tổng",
                };
                gks.Evidence.Add($"Mẫu dứt điểm của thủ môn: {Template(p.Position)[1]} điểm");
                list.Add(gks);
                goto Passing;
            }
            var sho = new AttrInsight
            {
                Key = "shooting", Name = "Dứt điểm", Icon = "bi-bullseye", Score = p.Shooting,
                Measure = g90.HasValue ? $"{N(g90.Value)} bàn / 90 phút" : $"{d.Goals + d.PrevGoals} bàn",
                MeasureIsReal = true,
                MeasureNote = g90.HasValue ? $"Hiệu suất ghi bàn ở {season}" : "Tổng số bàn thắng",
            };
            if (d.Minutes > 0) sho.Evidence.Add($"{season}: {d.Goals} bàn sau {d.Minutes} phút ({d.Apps} trận)");
            if (d.PrevSeason != null) sho.Evidence.Add($"{d.PrevSeason}: {d.PrevGoals} bàn" + (d.PrevPenalties > 0 ? $" ({d.PrevPenalties} quả phạt đền)" : ""));
            if (g90.HasValue && d.Goals > 0) sho.Evidence.Add($"Trung bình cứ {N(d.Minutes / (double)d.Goals, "0")} phút ghi 1 bàn");
            if (d.Minutes == 0 && d.PrevSeason == null) sho.Evidence.Add("Chưa có số liệu ghi bàn");
            list.Add(sho);

            Passing:
            // 3) Chuyền bóng
            var pas = new AttrInsight
            {
                Key = "passing", Name = "Chuyền bóng", Icon = "bi-arrow-left-right", Score = p.Passing,
                Measure = $"{p.Passing}/100", MeasureIsReal = false,
                MeasureNote = "Khả năng phân phối, kiến thiết bóng",
            };
            pas.Evidence.Add($"Mẫu chuyền bóng của vị trí {p.Position}: {Template(p.Position)[2]} điểm");
            if (d.Apps > 0) pas.Evidence.Add($"Được đá chính {d.Starts}/{d.Apps} trận ({N(d.Starts * 100.0 / d.Apps, "0")}%) – mức tin dùng làm tăng điểm");
            list.Add(pas);

            // 4) Rê bóng
            var dri = new AttrInsight
            {
                Key = "dribbling", Name = "Rê bóng", Icon = "bi-shuffle", Score = p.Dribbling,
                Measure = $"{p.Dribbling}/100", MeasureIsReal = false,
                MeasureNote = "Khả năng đi bóng, giữ bóng qua người",
            };
            dri.Evidence.Add($"Mẫu rê bóng của vị trí {p.Position}: {Template(p.Position)[3]} điểm");
            if (d.TeamMatches > 0) dri.Evidence.Add($"Thời gian thi đấu {N(d.Minutes * 100.0 / (d.TeamMatches * 90), "0")}% tổng số phút của đội");
            list.Add(dri);

            // 5) Phòng ngự (thủ môn: khả năng bắt bóng)
            var def = new AttrInsight
            {
                Key = "defending", Name = gk ? "Bắt bóng (phòng ngự)" : "Phòng ngự", Icon = "bi-shield", Score = p.Defending,
            };
            if (gk && d.Apps > 0)
            {
                var gc90 = per90(d.GoalsConceded) ?? 0;
                def = new AttrInsight
                {
                    Key = def.Key, Name = def.Name, Icon = def.Icon, Score = def.Score, MeasureIsReal = true,
                    Measure = $"{N(gc90)} bàn thua / 90 phút",
                    MeasureNote = $"Mùa {season}",
                };
                def.Evidence.Add($"{d.CleanSheets}/{d.Apps} trận giữ sạch lưới ({N(d.CleanSheets * 100.0 / d.Apps, "0")}%)");
                def.Evidence.Add($"Thủng lưới {d.GoalsConceded} bàn sau {d.Minutes} phút");
            }
            else if (d.TeamConcededPerGame.HasValue)
            {
                def = new AttrInsight
                {
                    Key = def.Key, Name = def.Name, Icon = def.Icon, Score = def.Score, MeasureIsReal = true,
                    Measure = $"Đội thủng lưới {N(d.TeamConcededPerGame.Value)} bàn / trận",
                    MeasureNote = $"Hàng thủ {d.TeamName} ở {season}",
                };
                def.Evidence.Add($"Mẫu phòng ngự của vị trí {p.Position}: {Template(p.Position)[4]} điểm");
                if (d.YellowCards + d.RedCards > 0) def.Evidence.Add($"{d.YellowCards} thẻ vàng, {d.RedCards} thẻ đỏ");
            }
            else
            {
                def = new AttrInsight { Key = def.Key, Name = def.Name, Icon = def.Icon, Score = def.Score, Measure = $"{p.Defending}/100", MeasureNote = "Khả năng tranh chấp, đánh chặn" };
                def.Evidence.Add($"Mẫu phòng ngự của vị trí {p.Position}: {Template(p.Position)[4]} điểm");
            }
            list.Add(def);

            // 6) Thể lực
            var phy = new AttrInsight
            {
                Key = "physical", Name = "Thể lực", Icon = "bi-heart-pulse", Score = p.Physical,
                Measure = d.Apps > 0 ? $"{N(d.Minutes / (double)d.Apps, "0")} phút / trận" : (p.HeightCm.HasValue ? $"{p.HeightCm} cm" : $"{p.Physical}/100"),
                MeasureIsReal = d.Apps > 0 || p.HeightCm.HasValue,
                MeasureNote = d.Apps > 0 ? $"Số phút trung bình mỗi trận ra sân ở {season}" : "Chiều cao",
            };
            if (d.Apps > 0) phy.Evidence.Add($"{d.Minutes} phút sau {d.Apps} trận, {d.Starts} trận đá chính");
            if (d.TeamMatches > 0) phy.Evidence.Add($"Có mặt {N(d.Minutes * 100.0 / (d.TeamMatches * 90), "0")}% thời gian thi đấu của đội");
            if (p.HeightCm.HasValue) phy.Evidence.Add($"Chiều cao {p.HeightCm} cm" + (p.HeightCm >= 183 ? " – lợi thế tranh chấp tay đôi" : ""));
            phy.Evidence.Add($"Tuổi {p.Age}");
            list.Add(phy);
            return list;
        }

        /// <summary>Hồ sơ chỉ số mẫu theo vị trí (tốc độ, dứt điểm, chuyền, rê, phòng ngự, thể lực) – trùng với tools/build_seed.py.</summary>
        public static int[] Template(Position p) => p switch
        {
            Position.GK => new[] { 45, 20, 52, 35, 66, 64 },
            Position.CB => new[] { 56, 32, 52, 45, 68, 70 },
            Position.LB or Position.RB => new[] { 68, 40, 58, 58, 62, 64 },
            Position.DM => new[] { 58, 45, 63, 57, 66, 68 },
            Position.CM => new[] { 60, 52, 66, 62, 56, 64 },
            Position.AM => new[] { 64, 60, 68, 68, 40, 58 },
            Position.LW or Position.RW => new[] { 72, 58, 60, 68, 36, 58 },
            _ => new[] { 66, 64, 52, 60, 32, 68 }
        };

        /// <summary>Nhãn ngắn gọn có đơn vị cho bảng so sánh / danh sách, ví dụ “72 (≈ 32,6 km/h)”.</summary>
        public static string Short(AttrInsight a) => a.Key == "pace" || a.MeasureIsReal ? $"{a.Score} · {a.Measure}" : $"{a.Score}";
    }
}

namespace ScoutVN.Services
{
    public static class PlayerInsightsText
    {
        /// <summary>Mô tả trọng số tính chỉ số tổng theo vị trí, ví dụ “dứt điểm 40%, tốc độ 20%…”.</summary>
        public static string Weights(ScoutVN.Models.Position pos)
        {
            string[] names = { "tốc độ", "dứt điểm", "chuyền bóng", "rê bóng", "phòng ngự", "thể lực" };
            double[] w = pos switch
            {
                ScoutVN.Models.Position.GK => new[] { 0.10, 0.00, 0.20, 0.05, 0.40, 0.25 },
                ScoutVN.Models.Position.CB => new[] { 0.10, 0.00, 0.10, 0.05, 0.45, 0.30 },
                ScoutVN.Models.Position.LB or ScoutVN.Models.Position.RB => new[] { 0.25, 0.00, 0.15, 0.10, 0.30, 0.20 },
                ScoutVN.Models.Position.DM => new[] { 0.05, 0.05, 0.25, 0.10, 0.35, 0.20 },
                ScoutVN.Models.Position.CM => new[] { 0.10, 0.10, 0.30, 0.20, 0.15, 0.15 },
                ScoutVN.Models.Position.AM => new[] { 0.10, 0.20, 0.30, 0.30, 0.00, 0.10 },
                ScoutVN.Models.Position.LW or ScoutVN.Models.Position.RW => new[] { 0.30, 0.20, 0.15, 0.30, 0.00, 0.05 },
                _ => new[] { 0.20, 0.40, 0.05, 0.20, 0.00, 0.15 }
            };
            return string.Join(", ", w.Select((x, i) => (x, n: names[i])).Where(t => t.x > 0).OrderByDescending(t => t.x).Select(t => $"{t.n} {t.x * 100:0}%"));
        }
    }
}
