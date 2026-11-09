using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using Match = ScoutVN.Models.Match;

namespace ScoutVN.Services
{
    /// <summary>Câu trả lời dạng thống kê: tóm tắt + bảng số liệu (mỗi dòng có thể kèm đường dẫn).</summary>
    public class StatsAnswer
    {
        public string Understood { get; set; } = "";
        public string Answer { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public List<string?> Links { get; set; } = new();
    }

    public interface IStatsQaService
    {
        /// <summary>Trả lời câu hỏi tra cứu thống kê (ai ghi nhiều bàn nhất, đội nào dẫn đầu, X bao nhiêu tuổi…).
        /// Trả về null nếu câu hỏi không thuộc loại tra cứu (để trợ lý chuyển sang gợi ý KNN).</summary>
        Task<StatsAnswer?> TryAnswerAsync(string question);
    }

    /// <summary>
    /// Trả lời câu hỏi tra cứu số liệu bằng luật (không cần mô hình ngôn ngữ): nhận diện loại câu hỏi,
    /// bộ lọc (mùa giải, đội, vị trí, độ tuổi, nội / ngoại binh, số lượng) rồi truy vấn trực tiếp CSDL.
    /// </summary>
    public class StatsQaService : IStatsQaService
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;
        private readonly IClubValuationService _valuation;
        public StatsQaService(AppDbContext db, IAnalysisService analysis, IClubValuationService valuation) { _db = db; _analysis = analysis; _valuation = valuation; }

        private static readonly CultureInfo VI = CultureInfo.GetCultureInfo("vi-VN");
        private static string N(double v, string f = "0.00") => v.ToString(f, VI);
        public static string Plain(string? s) => " " + Regex.Replace(TextHelper.RemoveDiacritics(s).ToLowerInvariant(), @"[^a-z0-9/ ]+", " ").Trim() + " ";
        private static bool Has(string s, string pattern) => Regex.IsMatch(s, pattern);

        // Bí danh đội bóng (không dấu) → tên viết tắt trong CSDL
        internal static readonly (string Short, string[] Alias)[] TeamAlias =
        {
            ("CAHN", new[] { "cong an ha noi", "cahn" }),
            ("CATP", new[] { "cong an tp ho chi minh", "cong an thanh pho ho chi minh", "cong an tp hcm", "cong an hcm", "cong an tphcm", "catp" }),
            ("TCVT", new[] { "the cong viettel", "the cong", "viettel" }),
            ("HNFC", new[] { "ha noi fc", "hanoi fc", "clb ha noi", "doi ha noi", "hnfc" }),
            ("TXND", new[] { "thep xanh nam dinh", "nam dinh", "txnd" }),
            ("HPFC", new[] { "hai phong", "hpfc" }),
            ("HLHT", new[] { "hong linh ha tinh", "ha tinh", "hlht" }),
            ("SLNA", new[] { "song lam nghe an", "song lam", "nghe an", "slna" }),
            ("HAGL", new[] { "hoang anh gia lai", "gia lai", "hagl" }),
            ("THFC", new[] { "dong a thanh hoa", "thanh hoa", "thfc" }),
            ("SHBĐN", new[] { "shb da nang", "da nang", "shb" }),
            ("PVF", new[] { "pvf cand", "pvf" }),
            ("BHCM", new[] { "becamex", "binh duong" }),
            ("NBFC", new[] { "ninh binh", "nbfc" }),
            ("BNFC", new[] { "bac ninh", "bnfc" }),
            ("TTĐN", new[] { "truong tuoi dong nai", "dong nai" }),
        };

        private class Ctx
        {
            public List<Player> Players = new();
            public List<Team> Teams = new();
            public List<Tournament> Tours = new();
            public List<PlayerStat> Stats = new();
            public List<Match> Matches = new();
            public Tournament? Cur, Prev;
        }

        private async Task<Ctx> LoadAsync()
        {
            var c = new Ctx
            {
                Players = await _db.Players.AsNoTracking().Include(p => p.Team).ToListAsync(),
                Teams = await _db.Teams.AsNoTracking().ToListAsync(),
                Tours = await _db.Tournaments.AsNoTracking().ToListAsync(),
                Stats = await _db.PlayerStats.AsNoTracking().Include(s => s.Team).Include(s => s.Tournament).ToListAsync(),
                Matches = await _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam).ToListAsync(),
            };
            c.Cur = c.Tours.Where(t => t.HasMatchDetails).OrderByDescending(t => t.Season).FirstOrDefault() ?? c.Tours.OrderByDescending(t => t.Season).FirstOrDefault();
            c.Prev = c.Tours.Where(t => t != c.Cur).OrderByDescending(t => t.Season).FirstOrDefault();
            return c;
        }

        // ------------------------------------------------------------------
        public async Task<StatsAnswer?> TryAnswerAsync(string question)
        {
            var s = Plain(question);
            // Câu hỏi tuyển chọn / tìm cầu thủ tương tự để cho KNN xử lý
            if (Has(s, @" (giong|tuong tu|thay the) ")) return null;
            var superlative = Has(s, @" nhat |top \d| dan dau |vua pha luoi| bao nhieu | ai | nao | may | danh sach | bang xep hang | xep hang | thu may ");
            var ctx = await LoadAsync();
            if (ctx.Cur == null) return null;

            var season = Has(s, @" (mua truoc|mua giai truoc|nam ngoai|mua 2025/26|2025/26|2025 26|2025/2026|mua 2025) ") ? ctx.Prev ?? ctx.Cur
                       : Has(s, @" (ca hai mua|hai mua|tat ca cac mua|tong cong|toan bo) ") ? null : ctx.Cur;
            var teams = FindTeams(s, ctx);
            var mentioned = FindPlayers(MaskTeams(s), ctx);
            var recruit = Has(s, @" (tim|goi y|can|tuyen|de xuat|chon) ");

            // 1) So sánh 2 cầu thủ
            if (mentioned.Count >= 2 && Has(s, @" (so sanh|hay|voi|va|vs|hon) ")) return Compare(mentioned.Take(2).ToList(), ctx);

            // 1b) Định giá câu lạc bộ ("đội nào đắt giá nhất", "định giá HAGL")
            if (Has(s, @" (dinh gia|gia tri (doi|clb|cau lac bo|doi hinh)|doi (nao|bong nao) (dat gia|gia tri|giau)|clb nao (dat gia|gia tri)) "))
                return await ClubValues(teams, ctx);

            // 2) Bảng xếp hạng đội / đội dẫn đầu / vô địch / đội ghi nhiều bàn
            if (Has(s, @" (bang xep hang|xep hang doi|bxh|dan dau|dung dau|ngoi dau|xep cuoi|bet bang|cuoi bang|vo dich|dung thu|xep thu|thu may) ")
                || (Has(s, @" doi (nao|bong nao) ") && Has(s, @" (ghi nhieu ban|ghi it ban|thung luoi|thang nhieu|thua nhieu|hoa nhieu|it thua|manh nhat|yeu nhat|hang cong|hang thu) ")))
                return await Standings(s, season ?? ctx.Cur!, teams, ctx);

            // 3) Kết quả / lịch thi đấu
            if (Has(s, @" (ket qua|ti so|lich thi dau|lich dau|tran dau|tran gan nhat|tran toi|tran tiep theo|vong \d+) ") && !mentioned.Any())
                return Results(s, season ?? ctx.Cur!, teams, ctx);

            // 4) Tổng quan giải ("giải có bao nhiêu bàn thắng?")
            if (mentioned.Count == 0 && teams.Count == 0 && !Has(s, @" nhat ") &&
                Has(s, @" (bao nhieu ban thang|tong so ban|bao nhieu tran|bao nhieu doi|bao nhieu cau thu|tong quan|thong ke giai) "))
                return Overview(season ?? ctx.Cur!, ctx);

            // 4b) Nêu từ 2 tiêu chí chuyên môn trở lên ("nhanh và tì đè tốt") → tìm cầu thủ đạt CẢ các tiêu chí bằng KNN,
            //     không trả bảng xếp hạng theo một chỉ số
            if (mentioned.Count == 0 && CoachNeeds.DetectFromText(question).Count(n => n != "young") >= 2) return null;

            // 5) Bảng xếp hạng cá nhân theo chỉ số (câu tuyển chọn không có từ so sánh nhất → để KNN xử lý)
            var metric = DetectMetric(s);
            var strong = Has(s, @" nhat |top \d| vua pha luoi | dan dau | ai | bao nhieu | danh sach ");
            if (metric != null && (strong || (!recruit && mentioned.Count == 0)) && !(mentioned.Count == 1 && Has(s, @" (bao nhieu|may) ")))
                return Leaderboard(s, metric, season, teams, ctx);

            // 6) Thông tin một cầu thủ
            if (mentioned.Count >= 1 && !recruit) return PlayerInfo(s, mentioned[0], mentioned.Skip(1).ToList(), ctx);

            // 7) Thông tin đội bóng
            if (teams.Count >= 1 && Has(s, @" (hlv|huan luyen vien|doi hinh|bao nhieu cau thu|san nha|thong tin|gioi thieu|ngoai binh|vua pha luoi|ghi ban|the nao|ra sao|phong do) "))
                return await TeamInfo(teams[0], season ?? ctx.Cur!, ctx);
            if (teams.Count >= 1 && !recruit) return await TeamInfo(teams[0], season ?? ctx.Cur!, ctx);

            if (Has(s, @" (giai dau|v league|vleague) .*(the nao|ra sao|bao nhieu)"))
                return Overview(season ?? ctx.Cur!, ctx);

            return null;
        }

        private async Task<StatsAnswer> ClubValues(List<Team> teams, Ctx ctx)
        {
            var all = await _valuation.AllAsync();
            var list = all.Values.OrderBy(v => v.Rank).ToList();
            var a = new StatsAnswer { Understood = "tra cứu · định giá câu lạc bộ (tổng giá trị ước tính đội hình)", Columns = new() { "#", "Đội bóng", "Định giá", "Cầu thủ", "TB / cầu thủ", "Giá trị nhất" } };
            string Name(int id) => ctx.Teams.FirstOrDefault(t => t.Id == id)?.Name ?? "?";
            foreach (var v in list)
            {
                a.Rows.Add(new() { v.Rank.ToString(), Name(v.TeamId), Fmt.Money((int)v.Total), v.Players.ToString(), Fmt.Money((int)Math.Round(v.Average)), v.MostValuable.FirstOrDefault()?.FullName ?? "–" });
                a.Links.Add($"/Teams/Details/{v.TeamId}");
            }
            if (teams.Count > 0 && all.TryGetValue(teams[0].Id, out var t))
                a.Answer = $"**{teams[0].Name}** được định giá khoảng **{Fmt.Money((int)t.Total)}** (tổng giá trị ước tính {t.Players} cầu thủ), xếp thứ **{t.Rank}/{t.TeamCount}** giải. Cầu thủ giá trị nhất: {string.Join(", ", t.MostValuable.Select(p => $"{p.FullName} ({Fmt.Money(p.MarketValue)})"))}.";
            else if (list.Count > 0)
                a.Answer = $"Câu lạc bộ có định giá cao nhất là **{Name(list[0].TeamId)}** ≈ **{Fmt.Money((int)list[0].Total)}**, tiếp theo là {Name(list[1].TeamId)} ({Fmt.Money((int)list[1].Total)}) và {Name(list[2].TeamId)} ({Fmt.Money((int)list[2].Total)}). Bảng dưới liệt kê toàn bộ {list.Count} đội.";
            return a;
        }

        // ------------------------------------------------------------------
        /// <summary>Che tên đội trong câu để không nhầm thành tên cầu thủ (VD “Hoàng Anh Gia Lai” ≠ “… Hoàng Anh”).</summary>
        private static string MaskTeams(string s)
        {
            foreach (var (_, aliases) in TeamAlias)
                foreach (var a in aliases.OrderByDescending(x => x.Length))
                    s = s.Replace(" " + a + " ", " " + new string('#', a.Length) + " ");
            return s;
        }

        private static List<Team> FindTeams(string s, Ctx ctx)
        {
            var found = new List<(int Pos, Team T)>();
            var rest = s;
            foreach (var (shortName, aliases) in TeamAlias)
                foreach (var a in aliases.OrderByDescending(x => x.Length))
                {
                    var i = rest.IndexOf(" " + a + " ", StringComparison.Ordinal);
                    if (i < 0) continue;
                    var t = ctx.Teams.FirstOrDefault(x => x.ShortName == shortName);
                    if (t != null && found.All(f => f.T.Id != t.Id)) found.Add((i, t));
                    rest = rest.Remove(i + 1, a.Length).Insert(i + 1, new string('#', a.Length));
                    break;
                }
            return found.OrderBy(f => f.Pos).Select(f => f.T).ToList();
        }

        /// <summary>Tìm cầu thủ được nhắc tên: khớp họ tên đầy đủ (không dấu), hoặc ≥ 2 âm tiết cuối, hoặc tên riêng của ngoại binh.</summary>
        private static List<Player> FindPlayers(string s, Ctx ctx)
        {
            var hits = new List<(int Pos, int Len, Player P)>();
            foreach (var p in ctx.Players)
            {
                var full = Plain(p.FullName).Trim();
                if (full.Length < 3) continue;
                var i = s.IndexOf(" " + full + " ", StringComparison.Ordinal);
                if (i >= 0) { hits.Add((i, full.Length + 100, p)); continue; }
                var w = full.Split(' ');
                if (w.Length >= 3)
                {
                    var tail = string.Join(' ', w.Skip(w.Length - 2));
                    i = s.IndexOf(" " + tail + " ", StringComparison.Ordinal);
                    if (i >= 0) hits.Add((i, tail.Length, p));
                }
                else if (w.Length <= 2 && p.Nationality != "Việt Nam")
                {
                    // ngoại binh: gọi theo họ (Makarić, Tagueu) hoặc nghệ danh 1 từ (Lucão)
                    var last = w[^1];
                    if (last.Length >= 5)
                    {
                        i = s.IndexOf(" " + last + " ", StringComparison.Ordinal);
                        if (i >= 0) hits.Add((i, last.Length, p));
                    }
                }
            }
            // Mỗi vị trí trong câu chỉ giữ ứng viên khớp dài nhất; trùng tên thì ưu tiên cầu thủ đang thi đấu
            return hits.GroupBy(h => h.Pos).OrderBy(g => g.Key)
                .Select(g => g.OrderByDescending(h => h.Len).ThenBy(h => h.P.Status == PlayerStatus.LeftLeague ? 1 : 0).First().P)
                .GroupBy(p => p.Id).Select(g => g.First()).ToList();
        }

        private record Metric(string Key, string Name, string Unit, bool Asc = false, bool NeedMinutes = false);

        private static Metric? DetectMetric(string s)
        {
            if (Has(s, @" (vua pha luoi|ghi nhieu ban|ghi ban nhieu|nhieu ban thang|ghi duoc nhieu|chan sut|ban thang nhieu|top ghi ban|ghi ban|ban thang) ") && !Has(s, @" (ban\s*/\s*90|moi 90|hieu suat) ")) return new("goals", "Bàn thắng", "bàn");
            if (Has(s, @" (hieu suat ghi ban|ban\s*/\s*90|moi 90 phut) ")) return new("g90", "Bàn / 90 phút", "bàn/90'", NeedMinutes: true);
            if (Has(s, @" (da chinh) ")) return new("starts", "Số trận đá chính", "trận");
            if (Has(s, @" (thi dau nhieu|ra san nhieu|nhieu phut|so phut|choi nhieu|duoc su dung nhieu|thoi gian thi dau|da nhieu nhat|thi dau nhieu nhat|nhieu tran) ")) return new("minutes", "Số phút thi đấu", "phút");
            if (Has(s, @" (the do) ")) return new("red", "Thẻ đỏ", "thẻ đỏ");
            if (Has(s, @" (the vang) ")) return new("yellow", "Thẻ vàng", "thẻ vàng");
            if (Has(s, @" (the phat|nhieu the|thi dau xau|ky luat) ")) return new("cards", "Điểm thẻ phạt (vàng + 2 × đỏ)", "điểm");
            if (Has(s, @" (sach luoi|giu sach) ")) return new("cs", "Trận giữ sạch lưới", "trận");
            if (Has(s, @" (thung luoi it|it thung luoi|bat tot nhat|thu mon tot nhat) ")) return new("gc90", "Bàn thua / 90 phút", "bàn/90'", Asc: true, NeedMinutes: true);
            if (Has(s, @" (tre nhat|it tuoi nhat) ")) return new("young", "Tuổi", "tuổi", Asc: true);
            if (Has(s, @" (dat gia|gia tri cao|gia tri chuyen nhuong|dat nhat|dang gia nhat|gia tri nhat|gia tri lon) ")) return new("value", "Giá trị ước tính", "");
            if (Has(s, @" (lon tuoi nhat|nhieu tuoi nhat|cao tuoi nhat|gia nhat) ")) return new("old", "Tuổi", "tuổi");
            if (Has(s, @" (cao nhat|chieu cao) ") && !Has(s, @" chi so ")) return new("height", "Chiều cao", "cm");
            if (Has(s, @" (toc do|nhanh nhat) ")) return new("pace", "Tốc độ (≈ km/h quy đổi)", "km/h");
            if (Has(s, @" (chi so tong|chi so cao|danh gia cao|ovr|gioi nhat|xuat sac nhat|hay nhat) ")) return new("ovr", "Chỉ số tổng (ước tính)", "điểm");
            return null;
        }

        private static (int Top, PositionGroup? Group, int? MaxAge, int? MinAge, bool? Foreign) Filters(string s)
        {
            int top = 5;
            var m = Regex.Match(s, @"top\s*(\d{1,2})|(\d{1,2})\s*(cau thu|nguoi|ung vien|thu mon|tien dao|hau ve|tien ve|doi)");
            if (m.Success) top = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            top = Math.Clamp(top, 1, 15);
            PositionGroup? g = null;
            if (Has(s, @" (thu mon|thu thanh) ")) g = PositionGroup.GK;
            else if (Has(s, @" (hau ve|trung ve) ")) g = PositionGroup.DEF;
            else if (Has(s, @" (tien dao|trung phong|tien ve canh|cau thu canh) ")) g = PositionGroup.ATT;
            else if (Has(s, @" tien ve ")) g = PositionGroup.MID;
            int? maxAge = null, minAge = null;
            m = Regex.Match(s, @" u ?(\d{2}) "); if (m.Success) maxAge = int.Parse(m.Groups[1].Value);
            m = Regex.Match(s, @"(?:duoi|nho hon)\s*(\d{2})"); if (m.Success) maxAge = int.Parse(m.Groups[1].Value) - 1;
            m = Regex.Match(s, @"(?:tren|lon hon)\s*(\d{2})\s*tuoi"); if (m.Success) minAge = int.Parse(m.Groups[1].Value) + 1;
            if (maxAge == null && Has(s, @" (tre|tre tuoi) ") && !Has(s, @" tre nhat ")) maxAge = 23;
            bool? foreign = Has(s, @" (ngoai binh|nuoc ngoai|cau thu ngoai) ") ? true : Has(s, @" (cau thu noi|noi binh|viet nam|trong nuoc|cau thu viet) ") ? false : null;
            return (top, g, maxAge, minAge, foreign);
        }

        // ------------------------------------------------------------------
        private StatsAnswer Leaderboard(string s, Metric metric, Tournament? season, List<Team> teams, Ctx ctx)
        {
            var f = Filters(s);
            var noDetail = season != null && !season.HasMatchDetails;
            var needsMinutes = metric.Key is "minutes" or "starts" or "g90" or "cs" or "gc90";
            var note = "";
            if (noDetail && needsMinutes)
            {
                note = $"{season!.Name} {season.Season} không có số phút / số trận chi tiết nên hệ thống dùng {ctx.Cur!.Name} {ctx.Cur.Season}. ";
                season = ctx.Cur;
            }

            // Cộng dồn số liệu theo cầu thủ trong phạm vi mùa
            var stats = ctx.Stats.Where(x => season == null || x.TournamentId == season.Id).ToList();
            var agg = stats.GroupBy(x => x.PlayerId).ToDictionary(g => g.Key, g => new
            {
                Apps = g.Sum(x => x.Appearances), Starts = g.Sum(x => x.Starts), Min = g.Sum(x => x.MinutesPlayed), Goals = g.Sum(x => x.Goals),
                Pen = g.Sum(x => x.PenaltyGoals ?? 0), Y = g.Sum(x => x.YellowCards), R = g.Sum(x => x.RedCards), Cs = g.Sum(x => x.CleanSheets), Gc = g.Sum(x => x.GoalsConceded),
                Team = g.OrderByDescending(x => x.Tournament!.Season).First().Team
            });
            var pool = ctx.Players.Where(p => p.Status != PlayerStatus.Retired).ToList();
            if (f.Group.HasValue) pool = pool.Where(p => PositionGroups.Of(p.Position) == f.Group).ToList();
            if (metric.Key is "cs" or "gc90") pool = pool.Where(p => p.Position == Position.GK).ToList();
            if (f.MaxAge.HasValue) pool = pool.Where(p => p.Age <= f.MaxAge).ToList();
            if (f.MinAge.HasValue) pool = pool.Where(p => p.Age >= f.MinAge).ToList();
            if (f.Foreign == true) pool = pool.Where(p => p.Nationality != "Việt Nam").ToList();
            if (f.Foreign == false) pool = pool.Where(p => p.Nationality == "Việt Nam").ToList();
            if (teams.Count > 0)
            {
                var ids = teams.Select(t => t.Id).ToHashSet();
                pool = pool.Where(p => (agg.TryGetValue(p.Id, out var a) && a.Team != null ? ids.Contains(a.Team.Id) : p.TeamId.HasValue && ids.Contains(p.TeamId.Value))).ToList();
            }

            double? Val(Player p)
            {
                agg.TryGetValue(p.Id, out var a);
                return metric.Key switch
                {
                    "goals" => a?.Goals is > 0 ? a.Goals : null,
                    "g90" => a != null && a.Min >= KnnQuery.DefaultMinMinutes ? a.Goals * 90.0 / a.Min : null,
                    "minutes" => a?.Min is > 0 ? a.Min : null,
                    "starts" => a?.Starts is > 0 ? a.Starts : null,
                    "yellow" => a?.Y is > 0 ? a.Y : null,
                    "red" => a?.R is > 0 ? a.R : null,
                    "cards" => a != null && a.Y + a.R > 0 ? a.Y + 2 * a.R : null,
                    "cs" => a?.Apps > 0 ? a.Cs : null,
                    "gc90" => a != null && a.Min >= KnnQuery.DefaultMinMinutes ? a.Gc * 90.0 / a.Min : null,
                    "young" or "old" => p.Age,
                    "height" => p.HeightCm,
                    "value" => p.MarketValue,
                    "pace" => PlayerInsights.PaceKmh(p.Pace),
                    "ovr" => p.Overall,
                    _ => null
                };
            }
            var ranked = pool.Select(p => (P: p, V: Val(p))).Where(x => x.V.HasValue);
            ranked = metric.Asc ? ranked.OrderBy(x => x.V).ThenByDescending(x => agg.TryGetValue(x.P.Id, out var a) ? a.Min : 0)
                                : ranked.OrderByDescending(x => x.V).ThenBy(x => agg.TryGetValue(x.P.Id, out var a) ? a.Min : 0);
            var list = ranked.Take(f.Top).ToList();

            var scope = season == null ? "tất cả các mùa" : $"{season.Name} {season.Season}";
            var filt = new List<string>();
            if (teams.Count > 0) filt.Add(string.Join(", ", teams.Select(t => t.Name)));
            if (f.Group.HasValue) filt.Add(PositionGroups.Name(f.Group.Value).ToLower());
            if (f.MaxAge.HasValue) filt.Add($"≤ {f.MaxAge} tuổi");
            if (f.MinAge.HasValue) filt.Add($"≥ {f.MinAge} tuổi");
            if (f.Foreign == true) filt.Add("ngoại binh"); if (f.Foreign == false) filt.Add("cầu thủ Việt Nam");
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · {metric.Name.ToLower()} · {scope}" + (filt.Count > 0 ? " · " + string.Join(" · ", filt) : "") + $" · top {f.Top}",
                Columns = new() { "#", "Cầu thủ", "Đội", "Vị trí", "Tuổi", metric.Name, "Trận / phút" }
            };
            string Fmt(double v) => metric.Key switch { "g90" or "gc90" => N(v), "pace" => N(v, "0.0"), "value" => ScoutVN.Services.Fmt.Money((int)v), _ => v.ToString("0", VI) };
            int i = 1;
            foreach (var (p, v) in list)
            {
                agg.TryGetValue(p.Id, out var a);
                res.Rows.Add(new()
                {
                    (i++).ToString(), p.FullName, a?.Team?.ShortName ?? p.Team?.ShortName ?? "–", p.Position.ToString(), p.Age.ToString(),
                    Fmt(v!.Value) + (metric.Key == "goals" && a?.Pen > 0 ? $" ({a.Pen} pen)" : ""),
                    a != null && a.Apps > 0 ? $"{a.Apps} trận / {a.Min}'" : "–"
                });
                res.Links.Add($"/cau-thu/{p.Id}");
            }
            if (list.Count == 0) { res.Answer = note + $"Không có cầu thủ nào thỏa điều kiện ({res.Understood})."; return res; }
            var best = list[0];
            var tie = list.Where(x => Math.Abs(x.V!.Value - best.V!.Value) < 1e-9).Select(x => x.P.FullName).ToList();
            var sb = new StringBuilder(note);
            var lead = metric.Asc ? "thấp nhất" : "cao nhất";
            if (metric.Key == "goals") sb.Append(tie.Count > 1 ? $"Dẫn đầu danh sách ghi bàn ({scope}) là **{string.Join(", ", tie)}** cùng **{Fmt(best.V!.Value)} bàn**." : $"Cầu thủ ghi nhiều bàn nhất ({scope}) là **{best.P.FullName}** ({best.P.Team?.Name ?? "–"}) với **{Fmt(best.V!.Value)} bàn**.");
            else if (metric.Key == "minutes") sb.Append($"Cầu thủ thi đấu nhiều nhất ({scope}) là **{string.Join(", ", tie)}** với **{Fmt(best.V!.Value)} phút**" + (tie.Count > 1 ? $" (cùng chơi trọn vẹn mọi trận)." : "."));
            else if (metric.Key == "pace") sb.Append($"Cầu thủ có tốc độ quy đổi {lead} là **{best.P.FullName}** (≈ {Fmt(best.V!.Value)} km/h). Lưu ý: đây là giá trị quy đổi từ điểm tốc độ ước tính, không phải số đo GPS.");
            else sb.Append($"{metric.Name} {lead} ({scope}): **{string.Join(", ", tie.Take(4))}** – {Fmt(best.V!.Value)} {metric.Unit}.");
            if (list.Count > 1) sb.Append($" Bảng dưới liệt kê top {list.Count}.");
            res.Answer = sb.ToString();
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<StatsAnswer> Standings(string s, Tournament t, List<Team> teams, Ctx ctx)
        {
            var st = await _analysis.StandingsAsync(t.Id);
            var scope = $"{t.Name} {t.Season}";
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · bảng xếp hạng đội · {scope}",
                Columns = new() { "#", "Đội bóng", "Trận", "T-H-B", "Bàn thắng–thua", "Hiệu số", "Điểm" }
            };
            IEnumerable<StandingRow> rows = st;
            string head;
            if (Has(s, @" doi (nao|bong nao) ") && Has(s, @" ghi nhieu ban| hang cong "))
            { rows = st.OrderByDescending(r => r.GoalsFor); var r0 = rows.First(); head = $"Đội ghi nhiều bàn nhất ({scope}) là **{r0.Team.Name}** với **{r0.GoalsFor} bàn** sau {r0.Played} trận."; res.Understood += " · sắp theo bàn thắng"; }
            else if (Has(s, @" (thung luoi it|it thung luoi|hang thu|phong ngu tot) "))
            { rows = st.OrderBy(r => r.GoalsAgainst).ThenByDescending(r => r.Points); var r0 = rows.First(); head = $"Hàng thủ tốt nhất ({scope}) là **{r0.Team.Name}**, chỉ thủng lưới **{r0.GoalsAgainst} bàn** sau {r0.Played} trận."; res.Understood += " · sắp theo bàn thua"; }
            else if (Has(s, @" (thung luoi nhieu|thua nhieu) "))
            { rows = st.OrderByDescending(r => Has(s, " thung luoi ") ? r.GoalsAgainst : r.Lost); var r0 = rows.First(); head = $"**{r0.Team.Name}** là đội thủng lưới / thua nhiều nhất ({r0.GoalsAgainst} bàn thua, {r0.Lost} trận thua)."; }
            else if (Has(s, @" (xep cuoi|bet bang|cuoi bang|yeu nhat) "))
            { var r0 = st.Last(); head = $"Đội xếp cuối bảng ({scope}) là **{r0.Team.Name}** với {r0.Points} điểm sau {r0.Played} trận ({r0.Won}T-{r0.Drawn}H-{r0.Lost}B)."; }
            else if (teams.Count > 0)
            {
                var r0 = st.FirstOrDefault(r => r.Team.Id == teams[0].Id);
                head = r0 == null ? $"{teams[0].Name} không thi đấu ở {scope}."
                    : $"**{r0.Team.Name}** đang xếp **thứ {r0.Rank}/{st.Count}** ({scope}) với **{r0.Points} điểm** sau {r0.Played} trận ({r0.Won} thắng, {r0.Drawn} hòa, {r0.Lost} thua; bàn thắng–thua {r0.GoalsFor}–{r0.GoalsAgainst}). Phong độ 5 trận gần nhất: {string.Join(" ", r0.Form)}.";
            }
            else
            {
                var r0 = st.First(); var r1 = st.Skip(1).FirstOrDefault();
                var done = t.EndDate.HasValue && t.EndDate < DateTime.Today;
                head = (done ? $"**{r0.Team.Name}** vô địch {scope} với **{r0.Points} điểm**" : $"**{r0.Team.Name}** đang dẫn đầu {scope} với **{r0.Points} điểm** sau {r0.Played} trận")
                       + (r1 != null ? $", hơn {r1.Team.Name} {r0.Points - r1.Points} điểm." : ".");
            }
            foreach (var r in rows)
            {
                res.Rows.Add(new() { r.Rank.ToString(), r.Team.Name, r.Played.ToString(), $"{r.Won}-{r.Drawn}-{r.Lost}", $"{r.GoalsFor}–{r.GoalsAgainst}", (r.GoalDiff > 0 ? "+" : "") + r.GoalDiff, r.Points.ToString() });
                res.Links.Add($"/doi-bong/{r.Team.Id}");
            }
            res.Answer = head;
            return res;
        }

        // ------------------------------------------------------------------
        private StatsAnswer Results(string s, Tournament t, List<Team> teams, Ctx ctx)
        {
            var ms = ctx.Matches.Where(m => m.TournamentId == t.Id).ToList();
            var upcoming = Has(s, @" (lich|tran toi|tran tiep theo|sap toi) ");
            var m = Regex.Match(s, @" vong (\d+) ");
            string title; var noUpcoming = false;
            if (teams.Count > 0)
            {
                var id = teams[0].Id;
                ms = ms.Where(x => x.HomeTeamId == id || x.AwayTeamId == id).ToList();
                if (m.Success) ms = ms.Where(x => x.Round == int.Parse(m.Groups[1].Value)).ToList();
                var played = ms.Where(x => x.IsPlayed).OrderByDescending(x => x.KickOff).ToList();
                var next = ms.Where(x => !x.IsPlayed).OrderBy(x => x.KickOff).ToList();
                if (upcoming && next.Count == 0) noUpcoming = true;
                ms = upcoming && next.Count > 0 ? next.Take(5).ToList() : played.Take(Has(s, @" (tran gan nhat|tran vua) ") ? 1 : 5).ToList();
                title = upcoming && next.Count > 0 ? $"lịch thi đấu sắp tới của {teams[0].Name}" : $"kết quả gần nhất của {teams[0].Name}";
            }
            else
            {
                var round = m.Success ? int.Parse(m.Groups[1].Value) : ms.Where(x => x.IsPlayed).Select(x => x.Round).DefaultIfEmpty(1).Max();
                ms = ms.Where(x => x.Round == round).OrderBy(x => x.KickOff).ToList();
                title = $"kết quả vòng {round}";
            }
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · {title} · {t.Name} {t.Season}",
                Columns = new() { "Vòng", "Thời gian", "Đội nhà", "Tỉ số", "Đội khách" }
            };
            foreach (var x in ms)
            {
                res.Rows.Add(new() { x.Round.ToString(), x.KickOff.ToString("dd/MM/yyyy HH:mm"), x.HomeTeam!.Name, x.IsPlayed ? $"{x.HomeGoals} – {x.AwayGoals}" : "vs", x.AwayTeam!.Name });
                res.Links.Add($"/giai-dau/{t.Id}");
            }
            if (ms.Count == 0) { res.Answer = "Không tìm thấy trận đấu phù hợp."; return res; }
            var f = ms[0];
            res.Answer = (noUpcoming ? "Dữ liệu hiện chưa có lịch các vòng tiếp theo (cập nhật đến hết vòng đã đá). " : "") + (teams.Count > 0 && f.IsPlayed
                ? $"Trận gần nhất của **{teams[0].Name}**: vòng {f.Round}, {f.HomeTeam!.Name} **{f.HomeGoals} – {f.AwayGoals}** {f.AwayTeam!.Name} ({f.KickOff:dd/MM/yyyy})."
                : $"Có {ms.Count} trận ({title}, {t.Name} {t.Season}), tổng {ms.Where(x => x.IsPlayed).Sum(x => x.HomeGoals!.Value + x.AwayGoals!.Value)} bàn thắng.");
            return res;
        }

        // ------------------------------------------------------------------
        private StatsAnswer PlayerInfo(string q, Player p, List<Player> others, Ctx ctx)
        {
            var stats = ctx.Stats.Where(x => x.PlayerId == p.Id).OrderByDescending(x => x.Tournament!.Season).ToList();
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · thông tin cầu thủ {p.FullName}",
                Columns = new() { "Giải / mùa", "Đội", "Trận (đá chính)", "Phút", "Bàn", "Thẻ V/Đ" + (p.Position == Position.GK ? " · sạch lưới" : "") }
            };
            foreach (var x in stats)
            {
                var d = x.Tournament!.HasMatchDetails;
                res.Rows.Add(new()
                {
                    $"{x.Tournament.Name} {x.Tournament.Season}", x.Team?.ShortName ?? "–",
                    d ? $"{x.Appearances} ({x.Starts})" : "–", d ? x.MinutesPlayed.ToString() : "–",
                    x.Goals + (x.PenaltyGoals > 0 ? $" ({x.PenaltyGoals} pen)" : ""),
                    $"{x.YellowCards}/{x.RedCards}" + (p.Position == Position.GK && d ? $" · {x.CleanSheets}" : "")
                });
                res.Links.Add($"/cau-thu/{p.Id}");
            }
            var sb = new StringBuilder();
            var curS = stats.FirstOrDefault(x => x.Tournament!.HasMatchDetails);
            // Trả lời thẳng vào ý được hỏi trước, sau đó là hồ sơ tóm tắt
            if (Has(q, @" (bao nhieu ban|may ban|ghi bao nhieu|ghi duoc bao nhieu|so ban) "))
                sb.Append($"**{p.FullName}** đã ghi **{curS?.Goals ?? 0} bàn** ở {ctx.Cur!.Name} {ctx.Cur.Season}" + (stats.Count > 1 ? $" và tổng cộng **{stats.Sum(x => x.Goals)} bàn** trong hai mùa có trong hệ thống" : "") + ". ");
            else if (Has(q, @" (bao nhieu tuoi|may tuoi|sinh nam|ngay sinh) "))
                sb.Append($"**{p.FullName}** năm nay **{p.Age} tuổi** (sinh ngày {p.DateOfBirth:dd/MM/yyyy}). ");
            else if (Has(q, @" (cao bao nhieu|chieu cao) "))
                sb.Append(p.HeightCm.HasValue ? $"**{p.FullName}** cao **{p.HeightCm} cm**. " : $"Nguồn dữ liệu chưa có chiều cao của {p.FullName}. ");
            else if (Has(q, @" (doi nao|cho doi|clb nao|cau lac bo nao) "))
                sb.Append(p.Team != null ? $"**{p.FullName}** đang thi đấu cho **{p.Team.Name}**. " : $"{p.FullName} hiện không thuộc đội nào trong V.League ({p.Status.DisplayName().ToLower()}). ");
            else if (Has(q, @" (bao nhieu phut|thi dau bao nhieu|ra san bao nhieu|bao nhieu tran) "))
                sb.Append(curS is { Appearances: > 0 } ? $"**{p.FullName}** đã ra sân **{curS.Appearances} trận** ({curS.Starts} trận đá chính), tổng **{curS.MinutesPlayed} phút** ở {ctx.Cur!.Season}. " : $"{p.FullName} chưa ra sân ở {ctx.Cur!.Season}. ");
            else if (Has(q, @" (gia tri|bao nhieu tien|dat khong) "))
                sb.Append(p.MarketValue.HasValue ? $"Giá trị ước tính của **{p.FullName}** khoảng **{ScoutVN.Services.Fmt.Money(p.MarketValue)}**. " : $"Chưa có giá trị ước tính của {p.FullName}. ");
            else if (Has(q, @" (toc do|nhanh khong|nhanh the nao) "))
                sb.Append($"Tốc độ quy đổi của **{p.FullName}** khoảng **{N(PlayerInsights.PaceKmh(p.Pace), "0.0")} km/h** (điểm {p.Pace}/100 – giá trị ước tính, không phải số đo GPS). ");
            sb.Append($"**{p.FullName}** – {p.Position.DisplayName()}, {p.Age} tuổi (sinh {p.DateOfBirth:dd/MM/yyyy}), quốc tịch {p.Nationality}");
            sb.Append(p.Team != null ? $", đang thi đấu cho **{p.Team.Name}**" + (p.ShirtNumber.HasValue ? $" (số áo {p.ShirtNumber})" : "") : $", {p.Status.DisplayName().ToLower()}");
            sb.Append('.');
            if (p.HeightCm.HasValue) sb.Append($" Chiều cao {p.HeightCm} cm.");
            if (p.MarketValue.HasValue) sb.Append($" Giá trị ước tính khoảng {ScoutVN.Services.Fmt.Money(p.MarketValue)}.");
            var cur = stats.FirstOrDefault(x => x.Tournament!.HasMatchDetails);
            if (cur != null && cur.Appearances > 0)
                sb.Append($" Mùa {cur.Tournament!.Season}: {cur.Appearances} trận ({cur.Starts} đá chính), {cur.MinutesPlayed} phút, {cur.Goals} bàn" +
                          (cur.MinutesPlayed > 0 ? $" ({N(cur.Goals * 90.0 / cur.MinutesPlayed)} bàn/90')" : "") + ".");
            var total = stats.Sum(x => x.Goals);
            if (stats.Count > 1 && !Has(q, @" (bao nhieu ban|may ban|ghi bao nhieu|ghi duoc bao nhieu|so ban) ")) sb.Append($" Tổng cộng {total} bàn trong hệ thống.");
            sb.Append($" Tốc độ quy đổi ≈ {N(PlayerInsights.PaceKmh(p.Pace), "0.0")} km/h, chỉ số tổng {p.Overall}/100 (ước tính).");
            if (others.Count > 0) sb.Append($" (Câu hỏi còn nhắc tới {string.Join(", ", others.Select(o => o.FullName))} – hỏi “so sánh A và B” để xem bảng so sánh.)");
            res.Answer = sb.ToString();
            return res;
        }

        private StatsAnswer Compare(List<Player> ps, Ctx ctx)
        {
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · so sánh {string.Join(" và ", ps.Select(p => p.FullName))}",
                Columns = new List<string> { "Tiêu chí" }.Concat(ps.Select(p => p.FullName)).ToList()
            };
            var cur = ctx.Cur!;
            PlayerStat? S(Player p) => ctx.Stats.FirstOrDefault(x => x.PlayerId == p.Id && x.TournamentId == cur.Id);
            PlayerStat? Sp(Player p) => ctx.Prev == null ? null : ctx.Stats.FirstOrDefault(x => x.PlayerId == p.Id && x.TournamentId == ctx.Prev.Id);
            void Row(string name, Func<Player, string> f) { res.Rows.Add(new List<string> { name }.Concat(ps.Select(f)).ToList()); res.Links.Add(null); }
            Row("Đội", p => p.Team?.Name ?? "–");
            Row("Vị trí, tuổi", p => $"{p.Position}, {p.Age} tuổi");
            Row("Chiều cao", p => p.HeightCm.HasValue ? $"{p.HeightCm} cm" : "–");
            Row("Giá trị ước tính", p => ScoutVN.Services.Fmt.Money(p.MarketValue, false));
            Row($"Trận / đá chính {cur.Season}", p => S(p) is { Appearances: > 0 } x ? $"{x.Appearances} / {x.Starts}" : "–");
            Row($"Phút {cur.Season}", p => S(p)?.MinutesPlayed.ToString() ?? "–");
            Row($"Bàn {cur.Season}", p => S(p)?.Goals.ToString() ?? "0");
            Row($"Bàn / 90' {cur.Season}", p => S(p) is { MinutesPlayed: > 0 } x ? N(x.Goals * 90.0 / x.MinutesPlayed) : "–");
            if (ctx.Prev != null) Row($"Bàn {ctx.Prev.Season}", p => Sp(p)?.Goals.ToString() ?? "0");
            Row("Tốc độ (≈ km/h quy đổi)", p => N(PlayerInsights.PaceKmh(p.Pace), "0.0"));
            Row("Chỉ số tổng (ước tính)", p => p.Overall.ToString());
            var a = ps[0]; var b = ps[1];
            var ga = S(a)?.Goals ?? 0; var gb = S(b)?.Goals ?? 0;
            var ma = S(a)?.MinutesPlayed ?? 0; var mb = S(b)?.MinutesPlayed ?? 0;
            res.Answer = $"Mùa {cur.Season}: **{a.FullName}** {ga} bàn sau {ma} phút, **{b.FullName}** {gb} bàn sau {mb} phút. " +
                         (ga == gb ? "Hai cầu thủ ghi số bàn bằng nhau. " : $"{(ga > gb ? a.FullName : b.FullName)} ghi nhiều bàn hơn. ") +
                         (ma == mb ? "" : $"{(ma > mb ? a.FullName : b.FullName)} được sử dụng nhiều hơn. ") +
                         "Xem bảng so sánh chi tiết bên dưới hoặc mở trang So sánh cầu thủ.";
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<StatsAnswer> TeamInfo(Team t, Tournament season, Ctx ctx)
        {
            var st = await _analysis.StandingsAsync(season.Id);
            var row = st.FirstOrDefault(r => r.Team.Id == t.Id);
            var squad = ctx.Players.Where(p => p.TeamId == t.Id).ToList();
            var tstats = ctx.Stats.Where(x => x.TeamId == t.Id && x.TournamentId == season.Id).ToList();
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · thông tin đội {t.Name} · {season.Name} {season.Season}",
                Columns = new() { "Cầu thủ", "Vị trí", "Tuổi", "Trận", "Phút", "Bàn" }
            };
            foreach (var x in tstats.OrderByDescending(x => x.Goals).ThenByDescending(x => x.MinutesPlayed).Take(10))
            {
                var p = ctx.Players.First(pp => pp.Id == x.PlayerId);
                res.Rows.Add(new() { p.FullName, p.Position.ToString(), p.Age.ToString(), season.HasMatchDetails ? x.Appearances.ToString() : "–", season.HasMatchDetails ? x.MinutesPlayed.ToString() : "–", x.Goals.ToString() });
                res.Links.Add($"/cau-thu/{p.Id}");
            }
            var sb = new StringBuilder($"**{t.Name}** ({t.City}");
            if (!string.IsNullOrEmpty(t.Stadium)) sb.Append($", sân {t.Stadium}");
            sb.Append(')');
            if (!string.IsNullOrEmpty(t.HeadCoach)) sb.Append($", HLV trưởng **{t.HeadCoach}**");
            sb.Append($". Đội hình hiện có {squad.Count} cầu thủ ({squad.Count(p => p.Nationality != "Việt Nam")} ngoại binh)");
            if (squad.Count > 0) sb.Append($", tuổi trung bình {N(squad.Average(p => p.Age), "0.0")}");
            sb.Append('.');
            if (row != null) sb.Append($" {season.Name} {season.Season}: xếp **thứ {row.Rank}/{st.Count}**, {row.Points} điểm sau {row.Played} trận ({row.Won}T-{row.Drawn}H-{row.Lost}B), bàn thắng–thua {row.GoalsFor}–{row.GoalsAgainst}.");
            else sb.Append($" Đội không thi đấu ở {season.Name} {season.Season}.");
            var top = tstats.OrderByDescending(x => x.Goals).FirstOrDefault();
            if (top != null && top.Goals > 0) sb.Append($" Ghi nhiều bàn nhất đội: {ctx.Players.First(p => p.Id == top.PlayerId).FullName} ({top.Goals} bàn).");
            res.Answer = sb.ToString();
            return res;
        }

        private StatsAnswer Overview(Tournament t, Ctx ctx)
        {
            var ms = ctx.Matches.Where(m => m.TournamentId == t.Id && m.IsPlayed).ToList();
            var goals = ms.Sum(m => m.HomeGoals!.Value + m.AwayGoals!.Value);
            var teams = ms.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct().Count();
            var st = ctx.Stats.Where(x => x.TournamentId == t.Id).ToList();
            var res = new StatsAnswer
            {
                Understood = $"tra cứu · tổng quan {t.Name} {t.Season}",
                Columns = new() { "Chỉ tiêu", "Giá trị" }
            };
            void Row(string a, string b) { res.Rows.Add(new() { a, b }); res.Links.Add($"/giai-dau/{t.Id}"); }
            Row("Số đội", teams.ToString());
            Row("Trận đã đá", ms.Count.ToString());
            Row("Tổng bàn thắng", goals.ToString());
            Row("Bàn / trận", ms.Count > 0 ? N(goals / (double)ms.Count) : "–");
            Row("Đội nhà thắng / hòa / đội khách thắng", $"{ms.Count(m => m.HomeGoals > m.AwayGoals)} / {ms.Count(m => m.HomeGoals == m.AwayGoals)} / {ms.Count(m => m.HomeGoals < m.AwayGoals)}");
            Row("Thẻ vàng / thẻ đỏ", $"{st.Sum(x => x.YellowCards)} / {st.Sum(x => x.RedCards)}");
            Row("Cầu thủ có số liệu", st.Count.ToString());
            res.Answer = $"{t.Name} {t.Season}: {teams} đội, {ms.Count} trận đã đá, **{goals} bàn thắng** (trung bình {(ms.Count > 0 ? N(goals / (double)ms.Count) : "–")} bàn/trận).";
            return res;
        }
    }
}
