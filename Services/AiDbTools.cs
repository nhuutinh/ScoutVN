using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Data;
using ScoutVN.Models;
using Match = ScoutVN.Models.Match;

namespace ScoutVN.Services
{
    /// <summary>Kết quả một lần gọi công cụ: dữ liệu JSON gửi lại cho mô hình + bảng hiển thị cho người dùng.</summary>
    public class ToolResult
    {
        public JsonNode Data { get; set; } = new JsonObject();
        public string Summary { get; set; } = "";
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public List<string?> Links { get; set; } = new();
    }

    /// <summary>
    /// Bộ công cụ CHỈ ĐỌC cho mô hình ngôn ngữ tra cứu CSDL (function calling).
    /// Mô hình không viết SQL: nó chọn công cụ + tham số; máy chủ kiểm tra, giới hạn tham số và tự truy vấn bằng EF Core.
    /// </summary>
    public class AiDbTools
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisService _analysis;
        private readonly IKnnService _knn;
        private static readonly CultureInfo VI = Fmt.VI;

        private readonly IClubValuationService _valuation;

        public AiDbTools(AppDbContext db, IAnalysisService analysis, IKnnService knn, IClubValuationService valuation)
        {
            _db = db; _analysis = analysis; _knn = knn; _valuation = valuation;
        }

        // ------------------------------------------------------------------
        // Khai báo công cụ (JSON Schema dùng chung cho OpenAI và Gemini)
        // ------------------------------------------------------------------
        private const string GroupEnum = "[\"GK\",\"DEF\",\"MID\",\"ATT\"]";
        private const string SeasonProp = "\"season\":{\"type\":\"string\",\"enum\":[\"current\",\"previous\",\"all\"],\"description\":\"current = mùa đang đá (2026/27, có đủ số trận, phút); previous = mùa trước 2025/26 (chỉ có bàn thắng, phạt đền, thẻ); all = cộng dồn. Mặc định current\"}";

        public static readonly (string Name, string Description, string Parameters)[] Definitions =
        {
            ("search_players",
             "Tìm và xếp hạng cầu thủ V.League theo điều kiện và tiêu chí sắp xếp. Dùng cho câu hỏi kiểu: ai ghi nhiều bàn nhất, ai đá nhiều phút nhất, thủ môn giữ sạch lưới nhiều nhất, cầu thủ cao nhất, nhanh nhất, đắt giá nhất, trẻ nhất, cầu thủ của một đội...",
             "{\"type\":\"object\",\"properties\":{" +
             "\"position_group\":{\"type\":\"string\",\"enum\":" + GroupEnum + ",\"description\":\"GK thủ môn, DEF hậu vệ, MID tiền vệ, ATT tiền đạo và cầu thủ chạy cánh\"}," +
             "\"positions\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"GK\",\"CB\",\"LB\",\"RB\",\"DM\",\"CM\",\"AM\",\"LW\",\"RW\",\"ST\"]},\"description\":\"Vị trí cụ thể\"}," +
             "\"team\":{\"type\":\"string\",\"description\":\"Tên hoặc tên viết tắt đội bóng, VD Hà Nội, CAHN, HAGL\"}," +
             "\"nationality\":{\"type\":\"string\",\"enum\":[\"vn\",\"foreign\"],\"description\":\"vn = cầu thủ Việt Nam, foreign = ngoại binh\"}," +
             "\"min_age\":{\"type\":\"integer\"},\"max_age\":{\"type\":\"integer\"}," +
             "\"min_height\":{\"type\":\"integer\",\"description\":\"cm\"},\"max_height\":{\"type\":\"integer\",\"description\":\"cm\"}," +
             "\"min_speed_kmh\":{\"type\":\"number\",\"description\":\"Tốc độ nước rút tối thiểu (km/h, quy đổi)\"}," +
                          "\"foot\":{\"type\":\"string\",\"enum\":[\"Left\",\"Right\"],\"description\":\"Chân thuận\"}," +
             "\"max_value_billion\":{\"type\":\"number\",\"description\":\"Giá trị ước tính tối đa, tỷ VNĐ\"}," +
             "\"min_minutes\":{\"type\":\"integer\",\"description\":\"Số phút thi đấu tối thiểu (chỉ mùa current)\"}," +
             SeasonProp + "," +
             "\"sort_by\":{\"type\":\"string\",\"enum\":[\"goals\",\"goals_per90\",\"penalty_goals\",\"minutes\",\"appearances\",\"starts\",\"clean_sheets\",\"goals_conceded_per90\",\"yellow_cards\",\"red_cards\",\"value\",\"age\",\"height\",\"speed\",\"overall\"],\"description\":\"Tiêu chí sắp xếp. Mặc định goals\"}," +
             "\"order\":{\"type\":\"string\",\"enum\":[\"desc\",\"asc\"],\"description\":\"desc = cao xuống thấp (mặc định), asc = thấp lên cao (VD trẻ nhất: sort_by=age, order=asc)\"}," +
             "\"limit\":{\"type\":\"integer\",\"description\":\"Số dòng trả về, 1–20, mặc định 10\"}}}"),

            ("get_player",
             "Xem hồ sơ và số liệu đầy đủ của một cầu thủ theo tên (có dấu hoặc không dấu, có thể chỉ gõ tên gọi như 'Quang Hải', 'Makarić'): đội, vị trí, tuổi, chiều cao, chân thuận, giá trị, số liệu từng mùa, 6 chỉ số có đơn vị (tốc độ km/h...).",
             "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Tên cầu thủ\"}},\"required\":[\"name\"]}"),

            ("get_team",
             "Thông tin một đội bóng: HLV trưởng, sân nhà, thành phố, thứ hạng – điểm hiện tại, phong độ, cầu thủ ghi bàn nhiều nhất, số ngoại binh, định giá câu lạc bộ (tổng giá trị đội hình, hạng giá trị trong giải).",
             "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Tên hoặc tên viết tắt đội\"}," + SeasonProp + "},\"required\":[\"name\"]}"),

            ("get_standings",
             "Bảng xếp hạng V.League (tính từ kết quả các trận đã đá): hạng, số trận, thắng-hòa-thua, bàn thắng-bàn thua, điểm.",
             "{\"type\":\"object\",\"properties\":{" + SeasonProp + "}}"),

            ("get_matches",
             "Lịch thi đấu / kết quả trận đấu, lọc theo đội, vòng đấu, mùa.",
             "{\"type\":\"object\",\"properties\":{\"team\":{\"type\":\"string\"},\"round\":{\"type\":\"integer\"}," + SeasonProp + "," +
             "\"status\":{\"type\":\"string\",\"enum\":[\"played\",\"upcoming\",\"all\"],\"description\":\"Mặc định all\"},\"limit\":{\"type\":\"integer\",\"description\":\"1–20, mặc định 10\"}}}"),

            ("recommend_players",
             "Gợi ý cầu thủ bằng thuật toán KNN theo nhu cầu chuyên môn của HLV, hoặc tìm cầu thủ có lối chơi tương tự một cầu thủ mẫu. Dùng khi HLV muốn TÌM / TUYỂN / GỢI Ý cầu thủ. BẮT BUỘC đưa vào needs ĐỦ MỌI tiêu chí HLV nêu (VD 'nhanh và tì đè tốt' → [\"speed\",\"strength\"]); kết quả xếp theo mức đạt đồng thời tất cả tiêu chí. Khi trả lời, nêu giá trị của từng tiêu chí cho mỗi cầu thủ.",
             "{\"type\":\"object\",\"properties\":{" +
             "\"position_group\":{\"type\":\"string\",\"enum\":" + GroupEnum + "}," +
             "\"needs\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"speed\",\"finishing\",\"passing\",\"dribbling\",\"defending\",\"strength\",\"aerial\",\"stamina\",\"reflex\",\"cleansheet\",\"young\",\"discipline\"]}," +
             "\"description\":\"speed tốc độ; finishing dứt điểm săn bàn; passing chuyền bóng kiến thiết; dribbling rê dắt; defending phòng ngự; strength tì đè tranh chấp; aerial không chiến chiều cao; stamina bền bỉ ra sân thường xuyên; reflex phản xạ (thủ môn); cleansheet giữ sạch lưới (thủ môn); young trẻ tiềm năng; discipline ít thẻ\"}," +
             "\"similar_to\":{\"type\":\"string\",\"description\":\"Tên cầu thủ mẫu (nếu tìm cầu thủ tương tự)\"}," +
             "\"min_height\":{\"type\":\"integer\"},\"min_speed_kmh\":{\"type\":\"number\"},\"foot\":{\"type\":\"string\",\"enum\":[\"Left\",\"Right\"]}," +
             "\"nationality\":{\"type\":\"string\",\"enum\":[\"vn\",\"foreign\"]},\"max_value_billion\":{\"type\":\"number\"}," +
             "\"min_age\":{\"type\":\"integer\"},\"max_age\":{\"type\":\"integer\"},\"k\":{\"type\":\"integer\",\"description\":\"Số gợi ý 1–10, mặc định 5\"}}}"),

            ("league_overview",
             "Tổng quan giải: số đội, số trận đã đá, tổng bàn thắng, trung bình bàn/trận, tỉ lệ thắng sân nhà, số thẻ, vua phá lưới.",
             "{\"type\":\"object\",\"properties\":{" + SeasonProp + "}}"),
        };

        // ------------------------------------------------------------------
        public async Task<ToolResult> ExecuteAsync(string name, JsonObject args)
        {
            try
            {
                return name switch
                {
                    "search_players" => await SearchPlayers(args),
                    "get_player" => await GetPlayer(args),
                    "get_team" => await GetTeam(args),
                    "get_standings" => await GetStandings(args),
                    "get_matches" => await GetMatches(args),
                    "recommend_players" => await Recommend(args),
                    "league_overview" => await Overview(args),
                    _ => Error($"Không có công cụ '{name}'.")
                };
            }
            catch (Exception ex)
            {
                return Error("Lỗi khi tra cứu: " + ex.Message);
            }
        }

        private static ToolResult Error(string msg) => new() { Data = new JsonObject { ["error"] = msg }, Summary = msg };

        // ===== Đọc tham số an toàn =====
        private static string? Str(JsonObject a, string k) => a.TryGetPropertyValue(k, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        private static double? Num(JsonObject a, string k)
        {
            if (!a.TryGetPropertyValue(k, out var v) || v is not JsonValue jv) return null;
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
            return null;
        }
        private static int? Int(JsonObject a, string k) => Num(a, k) is double d ? (int)Math.Round(d) : null;
        private static List<string> StrList(JsonObject a, string k)
            => a.TryGetPropertyValue(k, out var v) && v is JsonArray arr
                ? arr.Select(x => x is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null).Where(s => s != null).Select(s => s!).ToList()
                : new();
        private static PositionGroup? Group(JsonObject a) => Enum.TryParse<PositionGroup>(Str(a, "position_group"), true, out var g) ? g : null;

        private record Seasons(Tournament? Cur, Tournament? Prev);
        private async Task<Seasons> SeasonsAsync()
        {
            var tours = await _db.Tournaments.AsNoTracking().ToListAsync();
            var cur = tours.Where(t => t.HasMatchDetails).OrderByDescending(t => t.Season).FirstOrDefault() ?? tours.OrderByDescending(t => t.Season).FirstOrDefault();
            var prev = tours.Where(t => t != cur).OrderByDescending(t => t.Season).FirstOrDefault();
            return new(cur, prev);
        }
        private static (int Tid, string Label) Pick(Seasons s, string? season) => season switch
        {
            "previous" when s.Prev != null => (s.Prev.Id, $"{s.Prev.Name} {s.Prev.Season}"),
            "all" => (0, "cộng dồn các mùa"),
            _ => (s.Cur?.Id ?? 0, s.Cur == null ? "" : $"{s.Cur.Name} {s.Cur.Season}")
        };

        private async Task<Team?> FindTeamAsync(string? name)
        {
            if (name == null) return null;
            var q = StatsQaService.Plain(name).Trim();
            var teams = await _db.Teams.AsNoTracking().ToListAsync();
            foreach (var (shortName, aliases) in StatsQaService.TeamAlias)
                if (aliases.Any(al => q == al || q.Contains(al)) || StatsQaService.Plain(shortName).Trim() == q)
                    return teams.FirstOrDefault(t => t.ShortName == shortName);
            return teams.FirstOrDefault(t => StatsQaService.Plain(t.Name).Contains(" " + q + " ") || StatsQaService.Plain(t.ShortName).Trim() == q);
        }

        private static List<Player> MatchPlayers(IEnumerable<Player> players, string name)
        {
            var q = StatsQaService.Plain(name).Trim();
            var list = players.ToList();
            var exact = list.Where(p => StatsQaService.Plain(p.FullName).Trim() == q).ToList();
            if (exact.Count > 0) return exact;
            var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // Tên gọi ở cuối họ tên ("quang hai" ⊂ "nguyen quang hai") hoặc đủ mọi từ
            var tail = list.Where(p => StatsQaService.Plain(p.FullName).EndsWith(" " + q + " ")).ToList();
            if (tail.Count > 0) return tail;
            return list.Where(p => { var f = StatsQaService.Plain(p.FullName); return words.All(w => f.Contains(" " + w + " ")); }).ToList();
        }

        private static string N(double v, string f = "0.00") => v.ToString(f, VI);
        private static string Link(Player p) => $"/Players/Details/{p.Id}";

        // ------------------------------------------------------------------
        private async Task<ToolResult> SearchPlayers(JsonObject a)
        {
            var seasons = await SeasonsAsync();
            var season = Str(a, "season");
            var (tid, label) = Pick(seasons, season);
            var team = await FindTeamAsync(Str(a, "team"));
            if (Str(a, "team") != null && team == null) return Error($"Không tìm thấy đội '{Str(a, "team")}'.");
            var rows = await _analysis.LoadRowsAsync(tid, Group(a), team?.Id, Int(a, "min_age"), Int(a, "max_age"));
            var positions = StrList(a, "positions").Select(x => Enum.TryParse<Position>(x, true, out var p) ? p : (Position?)null).Where(p => p.HasValue).Select(p => p!.Value).ToHashSet();
            if (positions.Count > 0) rows = rows.Where(r => positions.Contains(r.Player.Position)).ToList();
            var f = new NeedFilter
            {
                MinHeight = Int(a, "min_height"), MinSpeed = Num(a, "min_speed_kmh"), Foot = Str(a, "foot"),
                Origin = Str(a, "nationality"), MaxValue = Num(a, "max_value_billion")
            };
            rows = f.Apply(rows).ToList();
            var maxH = Int(a, "max_height");
            if (maxH.HasValue) rows = rows.Where(r => r.Player.HeightCm.HasValue && r.Player.HeightCm <= maxH).ToList();
            var minMin = Int(a, "min_minutes");
            if (minMin.HasValue) rows = rows.Where(r => (r.Stat?.Minutes ?? 0) >= minMin).ToList();

            var sort = Str(a, "sort_by") ?? "goals";
            var asc = Str(a, "order") == "asc";
            var limit = Math.Clamp(Int(a, "limit") ?? 10, 1, 20);
            Func<PlayerRow, double?> key = sort switch
            {
                "goals_per90" => r => r.Stat is { Minutes: >= 180 } s ? s.Per90(s.Goals) : null,
                "penalty_goals" => r => r.Stat?.PenaltyGoals,
                "minutes" => r => r.Stat is { Minutes: > 0 } s ? s.Minutes : null,
                "appearances" => r => r.Stat is { Appearances: > 0 } s ? s.Appearances : null,
                "starts" => r => r.Stat is { Appearances: > 0 } s ? s.Starts : null,
                "clean_sheets" => r => r.Player.Position == Position.GK ? r.Stat?.CleanSheets : null,
                "goals_conceded_per90" => r => r.Player.Position == Position.GK && r.Stat is { Minutes: >= 180 } s ? s.Per90(s.GoalsConceded) : null,
                "yellow_cards" => r => r.Stat?.YellowCards,
                "red_cards" => r => r.Stat?.RedCards,
                "value" => r => r.Player.MarketValue,
                "age" => r => r.Player.Age,
                "height" => r => r.Player.HeightCm,
                "speed" => r => r.Player.Pace,
                "overall" => r => r.Player.Overall,
                _ => r => r.Stat?.Goals
            };
            var ranked = rows.Select(r => (R: r, V: key(r))).Where(x => x.V.HasValue);
            ranked = asc ? ranked.OrderBy(x => x.V).ThenByDescending(x => x.R.Stat?.Minutes ?? 0)
                         : ranked.OrderByDescending(x => x.V).ThenBy(x => x.R.Stat?.Minutes ?? 0);
            var top = ranked.Take(limit).ToList();

            var res = new ToolResult
            {
                Summary = $"search_players: {sort} {(asc ? "tăng" : "giảm")} · {label}" + (team != null ? $" · {team.ShortName}" : ""),
                Columns = new() { "#", "Cầu thủ", "Đội", "VT", "Tuổi", "Cao", "Trận", "Phút", "Bàn", "Sạch lưới", "Thẻ V/Đ", "Giá trị" }
            };
            var arr = new JsonArray();
            int i = 1;
            foreach (var (r, _) in top)
            {
                var p = r.Player; var s = r.Stat;
                arr.Add(new JsonObject
                {
                    ["name"] = p.FullName, ["team"] = p.Team?.Name ?? "Tự do", ["position"] = p.Position.ToString(), ["age"] = p.Age,
                    ["height_cm"] = p.HeightCm, ["nationality"] = p.Nationality, ["foot"] = p.PreferredFoot.ToString(),
                    ["appearances"] = s?.Appearances, ["starts"] = s?.Starts, ["minutes"] = s?.Minutes, ["goals"] = s?.Goals,
                    ["penalty_goals"] = s?.PenaltyGoals, ["goals_per90"] = s?.Per90(s.Goals) is double g ? Math.Round(g, 2) : null,
                    ["clean_sheets"] = p.Position == Position.GK ? s?.CleanSheets : null,
                    ["goals_conceded"] = p.Position == Position.GK ? s?.GoalsConceded : null,
                    ["yellow_cards"] = s?.YellowCards, ["red_cards"] = s?.RedCards,
                    ["speed_kmh_estimated"] = PlayerInsights.PaceKmh(p.Pace), ["overall_estimated"] = p.Overall,
                    ["value_estimated"] = Fmt.Money(p.MarketValue)
                });
                res.Rows.Add(new()
                {
                    (i++).ToString(), p.FullName, p.Team?.ShortName ?? "Tự do", p.Position.ToString(), p.Age.ToString(),
                    p.HeightCm.HasValue ? $"{p.HeightCm} cm" : "–",
                    s is { Appearances: > 0 } ? s.Appearances.ToString() : "–", s is { Minutes: > 0 } ? s.Minutes.ToString("N0", VI) : "–",
                    s?.Goals.ToString() ?? "0", p.Position == Position.GK && s != null ? s.CleanSheets.ToString() : "–",
                    s == null ? "–" : $"{s.YellowCards}/{s.RedCards}", Fmt.Money(p.MarketValue, false)
                });
                res.Links.Add(Link(p));
            }
            res.Data = new JsonObject
            {
                ["season"] = label, ["sorted_by"] = sort, ["order"] = asc ? "asc" : "desc",
                ["total_matching"] = ranked.Count(), ["players"] = arr,
                ["note"] = season == "previous" ? "Mùa trước chỉ có bàn thắng, phạt đền, thẻ (không có số trận, phút)." : "speed_kmh_estimated là quy đổi từ điểm tốc độ, value_estimated là giá trị ước tính."
            };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> GetPlayer(JsonObject a)
        {
            var name = Str(a, "name");
            if (name == null) return Error("Thiếu tên cầu thủ.");
            var players = await _db.Players.AsNoTracking().Include(p => p.Team).ToListAsync();
            var hits = MatchPlayers(players, name);
            if (hits.Count == 0) return Error($"Không tìm thấy cầu thủ '{name}'.");
            if (hits.Count > 1 && hits.Select(h => h.FullName).Distinct().Count() > 1)
            {
                var amb = new ToolResult { Summary = $"get_player: '{name}' trùng {hits.Count} cầu thủ", Columns = new() { "Cầu thủ", "Đội", "VT", "Tuổi" } };
                var arr = new JsonArray();
                foreach (var h in hits.Take(8))
                {
                    arr.Add(new JsonObject { ["name"] = h.FullName, ["team"] = h.Team?.Name ?? "Tự do", ["position"] = h.Position.ToString(), ["age"] = h.Age });
                    amb.Rows.Add(new() { h.FullName, h.Team?.ShortName ?? "Tự do", h.Position.ToString(), h.Age.ToString() });
                    amb.Links.Add(Link(h));
                }
                amb.Data = new JsonObject { ["ambiguous"] = true, ["candidates"] = arr, ["hint"] = "Hỏi lại người dùng muốn cầu thủ nào, hoặc gọi lại với họ tên đầy đủ." };
                return amb;
            }
            var p = hits.OrderBy(h => h.Status == PlayerStatus.LeftLeague ? 1 : 0).First();
            var stats = await _db.PlayerStats.AsNoTracking().Include(s => s.Tournament).Include(s => s.Team).Where(s => s.PlayerId == p.Id).ToListAsync();
            var insights = await _analysis.InsightsAsync(p);
            var res = new ToolResult { Summary = $"get_player: {p.FullName}", Columns = new() { "Mùa", "Đội", "Trận", "Đá chính", "Phút", "Bàn", "Phạt đền", "Sạch lưới", "Thẻ V/Đ" } };
            var sArr = new JsonArray();
            foreach (var s in stats.OrderByDescending(s => s.Tournament!.Season))
            {
                var det = s.Tournament!.HasMatchDetails;
                sArr.Add(new JsonObject
                {
                    ["season"] = $"{s.Tournament.Name} {s.Tournament.Season}", ["team"] = s.Team?.Name,
                    ["appearances"] = det ? s.Appearances : null, ["starts"] = det ? s.Starts : null, ["minutes"] = det ? s.MinutesPlayed : null,
                    ["goals"] = s.Goals, ["penalty_goals"] = s.PenaltyGoals,
                    ["clean_sheets"] = p.Position == Position.GK && det ? s.CleanSheets : null,
                    ["goals_conceded"] = p.Position == Position.GK && det ? s.GoalsConceded : null,
                    ["yellow_cards"] = s.YellowCards, ["red_cards"] = s.RedCards
                });
                res.Rows.Add(new()
                {
                    s.Tournament.Season, s.Team?.ShortName ?? "–", det ? s.Appearances.ToString() : "–", det ? s.Starts.ToString() : "–",
                    det ? s.MinutesPlayed.ToString("N0", VI) : "–", s.Goals.ToString(), s.PenaltyGoals?.ToString() ?? "–",
                    p.Position == Position.GK && det ? s.CleanSheets.ToString() : "–", $"{s.YellowCards}/{s.RedCards}"
                });
                res.Links.Add(null);
            }
            res.Data = new JsonObject
            {
                ["name"] = p.FullName, ["team"] = p.Team?.Name ?? "Tự do", ["position"] = p.Position.ToString(), ["age"] = p.Age,
                ["date_of_birth"] = p.DateOfBirth.ToString("dd/MM/yyyy"), ["nationality"] = p.Nationality, ["height_cm"] = p.HeightCm,
                ["shirt_number"] = p.ShirtNumber, ["status"] = p.Status.ToString(),
                ["value_estimated"] = Fmt.Money(p.MarketValue), ["overall_estimated"] = p.Overall,
                ["attributes"] = new JsonArray(insights.Select(i => (JsonNode)new JsonObject
                {
                    ["name"] = i.Name, ["score_100"] = i.Score, ["level"] = i.Level, ["measure"] = i.Measure, ["note"] = i.MeasureNote
                }).ToArray()),
                ["seasons"] = sArr,
                ["profile_url"] = Link(p)
            };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> GetTeam(JsonObject a)
        {
            var team = await FindTeamAsync(Str(a, "name"));
            if (team == null) return Error($"Không tìm thấy đội '{Str(a, "name")}'.");
            var seasons = await SeasonsAsync();
            var (tid, label) = Pick(seasons, Str(a, "season"));
            var standing = tid == 0 ? null : (await _analysis.StandingsAsync(tid)).FirstOrDefault(r => r.Team.Id == team.Id);
            var rows = await _analysis.LoadRowsAsync(tid, teamId: team.Id);
            var squad = rows.Where(r => r.Player.Status != PlayerStatus.LeftLeague).ToList();
            var scorers = rows.Where(r => r.Stat?.Goals > 0).OrderByDescending(r => r.Stat!.Goals).Take(5).ToList();
            var cv = await _valuation.ForTeamAsync(team.Id);
            var res = new ToolResult { Summary = $"get_team: {team.ShortName} · {label}", Columns = new() { "Cầu thủ", "VT", "Tuổi", "Bàn", "Phút" } };
            foreach (var r in scorers)
            {
                res.Rows.Add(new() { r.Player.FullName, r.Player.Position.ToString(), r.Player.Age.ToString(), r.Stat!.Goals.ToString(), r.Stat.Minutes > 0 ? r.Stat.Minutes.ToString("N0", VI) : "–" });
                res.Links.Add(Link(r.Player));
            }
            res.Data = new JsonObject
            {
                ["name"] = team.Name, ["short_name"] = team.ShortName, ["city"] = team.City, ["stadium"] = team.Stadium,
                ["head_coach"] = team.HeadCoach, ["founded"] = team.FoundedYear, ["season"] = label,
                ["standing"] = standing == null ? null : new JsonObject
                {
                    ["rank"] = standing.Rank, ["played"] = standing.Played, ["won"] = standing.Won, ["drawn"] = standing.Drawn, ["lost"] = standing.Lost,
                    ["goals_for"] = standing.GoalsFor, ["goals_against"] = standing.GoalsAgainst, ["points"] = standing.Points,
                    ["form_recent"] = new string(standing.Form.ToArray())
                },
                ["club_value_estimated"] = cv == null ? null : new JsonObject
                {
                    ["total"] = Fmt.Money((int)cv.Total), ["rank_in_league"] = $"{cv.Rank}/{cv.TeamCount}",
                    ["average_per_player"] = Fmt.Money((int)Math.Round(cv.Average)),
                    ["most_valuable"] = string.Join(", ", cv.MostValuable.Select(p => $"{p.FullName} ({Fmt.Money(p.MarketValue)})"))
                },
                ["squad_size"] = squad.Count, ["foreign_players"] = squad.Count(r => r.Player.Nationality != "Việt Nam"),
                ["average_age"] = squad.Count == 0 ? null : Math.Round(squad.Average(r => r.Player.Age), 1),
                ["top_scorers"] = new JsonArray(scorers.Select(r => (JsonNode)new JsonObject { ["name"] = r.Player.FullName, ["goals"] = r.Stat!.Goals }).ToArray())
            };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> GetStandings(JsonObject a)
        {
            var seasons = await SeasonsAsync();
            var (tid, label) = Pick(seasons, Str(a, "season") == "all" ? "current" : Str(a, "season"));
            var table = await _analysis.StandingsAsync(tid);
            var res = new ToolResult { Summary = $"get_standings: {label}", Columns = new() { "#", "Đội bóng", "Trận", "T", "H", "B", "BT-BB", "Điểm" } };
            var arr = new JsonArray();
            foreach (var r in table)
            {
                arr.Add(new JsonObject { ["rank"] = r.Rank, ["team"] = r.Team.Name, ["played"] = r.Played, ["won"] = r.Won, ["drawn"] = r.Drawn, ["lost"] = r.Lost, ["goals_for"] = r.GoalsFor, ["goals_against"] = r.GoalsAgainst, ["points"] = r.Points });
                res.Rows.Add(new() { r.Rank.ToString(), r.Team.Name, r.Played.ToString(), r.Won.ToString(), r.Drawn.ToString(), r.Lost.ToString(), $"{r.GoalsFor}-{r.GoalsAgainst}", r.Points.ToString() });
                res.Links.Add($"/Portal/Team/{r.Team.Id}");
            }
            res.Data = new JsonObject { ["season"] = label, ["table"] = arr };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> GetMatches(JsonObject a)
        {
            var seasons = await SeasonsAsync();
            var (tid, label) = Pick(seasons, Str(a, "season") == "all" ? "current" : Str(a, "season"));
            var team = await FindTeamAsync(Str(a, "team"));
            if (Str(a, "team") != null && team == null) return Error($"Không tìm thấy đội '{Str(a, "team")}'.");
            var round = Int(a, "round");
            var status = Str(a, "status") ?? "all";
            var limit = Math.Clamp(Int(a, "limit") ?? 10, 1, 20);
            var q = _db.Matches.AsNoTracking().Include(m => m.HomeTeam).Include(m => m.AwayTeam).Where(m => m.TournamentId == tid);
            if (team != null) q = q.Where(m => m.HomeTeamId == team.Id || m.AwayTeamId == team.Id);
            if (round.HasValue) q = q.Where(m => m.Round == round);
            var list = await q.ToListAsync();
            if (status == "played") list = list.Where(m => m.IsPlayed).ToList();
            if (status == "upcoming") list = list.Where(m => !m.IsPlayed).ToList();
            // Trận đã đá: gần nhất trước; trận sắp tới: sớm nhất trước
            list = status == "upcoming" ? list.OrderBy(m => m.KickOff).ToList()
                 : round.HasValue ? list.OrderBy(m => m.KickOff).ToList()
                 : list.OrderBy(m => m.IsPlayed ? 1 : 0).ThenBy(m => m.IsPlayed ? -m.KickOff.Ticks : m.KickOff.Ticks).ToList();
            var take = list.Take(limit).ToList();
            var res = new ToolResult { Summary = $"get_matches: {label}" + (team != null ? $" · {team.ShortName}" : "") + (round.HasValue ? $" · vòng {round}" : ""), Columns = new() { "Vòng", "Ngày", "Đội nhà", "Tỉ số", "Đội khách" } };
            var arr = new JsonArray();
            foreach (var m in take)
            {
                var score = m.IsPlayed ? $"{m.HomeGoals}–{m.AwayGoals}" : "chưa đá";
                arr.Add(new JsonObject { ["round"] = m.Round, ["date"] = m.KickOff.ToString("dd/MM/yyyy HH:mm"), ["home"] = m.HomeTeam?.Name, ["away"] = m.AwayTeam?.Name, ["score"] = score });
                res.Rows.Add(new() { m.Round.ToString(), m.KickOff.ToString("dd/MM/yyyy"), m.HomeTeam?.ShortName ?? "", score, m.AwayTeam?.ShortName ?? "" });
                res.Links.Add(null);
            }
            res.Data = new JsonObject { ["season"] = label, ["total"] = list.Count, ["matches"] = arr, ["today"] = DateTime.Today.ToString("dd/MM/yyyy") };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> Recommend(JsonObject a)
        {
            var seasons = await SeasonsAsync();
            var tid = seasons.Cur?.Id ?? 0;
            var group = Group(a);
            var k = Math.Clamp(Int(a, "k") ?? 5, 1, 10);
            PlayerRow? sample = null;
            var sim = Str(a, "similar_to");
            var rowsAll = (await _analysis.LoadRowsAsync(tid)).Where(r => r.Stat != null).ToList();
            if (sim != null)
            {
                var hits = MatchPlayers(rowsAll.Select(r => r.Player), sim);
                if (hits.Count == 0) return Error($"Không tìm thấy cầu thủ mẫu '{sim}' trong mùa hiện tại.");
                sample = rowsAll.First(r => r.Player.Id == hits[0].Id);
                group ??= PositionGroups.Of(sample.Player.Position);
            }
            var needs = CoachNeeds.Normalize(StrList(a, "needs"), group);
            var filter = new NeedFilter
            {
                Needs = needs, MinHeight = Int(a, "min_height"), MinSpeed = Num(a, "min_speed_kmh"), Foot = Str(a, "foot"),
                Origin = Str(a, "nationality"), MaxValue = Num(a, "max_value_billion"), MinAge = Int(a, "min_age"), MaxAge = Int(a, "max_age")
            };
            if (needs.Contains("young") && filter.MaxAge == null) filter.MaxAge = 23;
            var weights = KnnService.WeightsFromNeeds(needs);
            if (weights.Values.Sum() <= 0) weights = KnnService.WeightsFromNeeds(needs.Concat(CoachNeeds.Defaults(group)));
            var q = new KnnQuery
            {
                Group = group, TournamentId = tid, MinAge = filter.MinAge, MaxAge = filter.MaxAge, K = k, Filter = filter, Weights = weights,
                Target = sample != null ? _knn.VectorOf(sample) : await _knn.SuggestTargetAsync(group, tid),
                ExcludePlayerId = sample?.Player.Id
            };
            var knn = await _knn.RecommendAsync(q);
            var cols = CoachNeeds.Columns(needs);
            var res = new ToolResult
            {
                Summary = "recommend_players: " + (sample != null ? $"giống {sample.Player.FullName} · " : "") + string.Join(", ", needs.Select(n => CoachNeeds.Find(n)?.Name)),
                Columns = new List<string> { "#", "Cầu thủ", "Đội", "Tuổi", "Phù hợp" }.Concat(cols.Select(c => c.Header)).ToList()
            };
            var arr = new JsonArray();
            int i = 1;
            foreach (var n in knn.Neighbors)
            {
                var p = n.Row.Player;
                var o = new JsonObject
                {
                    ["name"] = p.FullName, ["team"] = p.Team?.Name ?? "Tự do", ["position"] = p.Position.ToString(), ["age"] = p.Age,
                    ["match_percent"] = Math.Round(n.Match, 1), ["strengths"] = string.Join(", ", n.Strengths), ["gaps"] = string.Join(", ", n.Gaps)
                };
                foreach (var c in cols) o[c.Header] = c.Show(n.Row);
                arr.Add(o);
                res.Rows.Add(new List<string> { (i++).ToString(), p.FullName, p.Team?.ShortName ?? "Tự do", p.Age.ToString(), N(n.Match, "0") + "%" }.Concat(cols.Select(c => c.Show(n.Row))).ToList());
                res.Links.Add(Link(p));
            }
            res.Data = new JsonObject
            {
                ["method"] = "KNN, khoảng cách Euclid có trọng số trên dữ liệu chuẩn hóa Min–Max",
                ["position_group"] = group?.ToString(), ["needs"] = new JsonArray(needs.Select(x => (JsonNode)(CoachNeeds.Find(x)?.Name ?? x)).ToArray()),
                ["filters"] = string.Join(", ", filter.Describe()), ["similar_to"] = sample?.Player.FullName,
                ["candidates_considered"] = knn.PoolSize, ["recommendations"] = arr,
                ["page_url"] = "/Recommend"
            };
            return res;
        }

        // ------------------------------------------------------------------
        private async Task<ToolResult> Overview(JsonObject a)
        {
            var seasons = await SeasonsAsync();
            var (tid, label) = Pick(seasons, Str(a, "season") == "all" ? "current" : Str(a, "season"));
            var ms = await _db.Matches.AsNoTracking().Where(m => m.TournamentId == tid).ToListAsync();
            var played = ms.Where(m => m.IsPlayed).ToList();
            var goals = played.Sum(m => m.HomeGoals!.Value + m.AwayGoals!.Value);
            var stats = await _db.PlayerStats.AsNoTracking().Include(s => s.Player).ThenInclude(p => p!.Team).Where(s => s.TournamentId == tid).ToListAsync();
            var top = stats.OrderByDescending(s => s.Goals).Take(3).ToList();
            var teams = played.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct().Count();
            var res = new ToolResult
            {
                Summary = $"league_overview: {label}",
                Columns = new() { "Chỉ số", "Giá trị" },
                Rows = new()
                {
                    new() { "Số đội", teams.ToString() }, new() { "Trận đã đá / tổng", $"{played.Count}/{ms.Count}" },
                    new() { "Tổng bàn thắng", goals.ToString() }, new() { "Bàn / trận", played.Count == 0 ? "–" : N(goals / (double)played.Count) },
                    new() { "Thẻ vàng / đỏ", $"{stats.Sum(s => s.YellowCards)} / {stats.Sum(s => s.RedCards)}" }
                }
            };
            res.Links.AddRange(res.Rows.Select(_ => (string?)null));
            res.Data = new JsonObject
            {
                ["season"] = label, ["teams"] = teams, ["matches_played"] = played.Count, ["matches_total"] = ms.Count,
                ["total_goals"] = goals, ["goals_per_match"] = played.Count == 0 ? null : Math.Round(goals / (double)played.Count, 2),
                ["home_win_percent"] = played.Count == 0 ? null : Math.Round(played.Count(m => m.HomeGoals > m.AwayGoals) * 100.0 / played.Count, 1),
                ["draw_percent"] = played.Count == 0 ? null : Math.Round(played.Count(m => m.HomeGoals == m.AwayGoals) * 100.0 / played.Count, 1),
                ["yellow_cards"] = stats.Sum(s => s.YellowCards), ["red_cards"] = stats.Sum(s => s.RedCards),
                ["top_scorers"] = new JsonArray(top.Select(s => (JsonNode)new JsonObject { ["name"] = s.Player?.FullName, ["team"] = s.Player?.Team?.Name, ["goals"] = s.Goals }).ToArray())
            };
            return res;
        }
    }
}
