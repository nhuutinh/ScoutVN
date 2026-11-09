using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScoutVN.Models.ViewModels;

namespace ScoutVN.Services
{
    /// <summary>Cấu hình dịch vụ mô hình ngôn ngữ lớn (đọc từ appsettings / user-secrets, chỉ ở phía máy chủ).</summary>
    public class AiAssistantOptions
    {
        /// <summary>"gemini", "claude" (Anthropic Messages API) hoặc "openai" (mọi dịch vụ tương thích OpenAI Chat Completions).</summary>
        public string Provider { get; set; } = "gemini";
        public string? ApiKey { get; set; }
        public string? Model { get; set; }
        /// <summary>Địa chỉ API tùy chọn (để trống = địa chỉ mặc định của nhà cung cấp).</summary>
        public string? BaseUrl { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
        /// <summary>Số vòng gọi công cụ tối đa cho một câu hỏi (chống lặp vô hạn, giới hạn chi phí).</summary>
        public int MaxToolRounds { get; set; } = 5;
        /// <summary>Giá trị header anthropic-version khi dùng Claude.</summary>
        public string AnthropicVersion { get; set; } = "2023-06-01";
    }

    /// <summary>Yêu cầu tuyển chọn mà trợ lý trích được từ câu hỏi tiếng Việt.</summary>
    public class ScoutIntent
    {
        public PositionGroup? Group { get; set; }
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int K { get; set; } = 5;
        public List<string> Emphasis { get; set; } = new();
        /// <summary>Mọi nhu cầu chuyên môn HLV nêu trong câu (CoachNeeds), VD tốc độ + tì đè.</summary>
        public List<string> Needs { get; set; } = new();
        public PlayerRow? SimilarTo { get; set; }

        public string Describe()
        {
            var parts = new List<string>();
            if (SimilarTo != null) parts.Add($"tương tự {SimilarTo.Player.FullName}");
            parts.Add(Group.HasValue ? PositionGroups.Name(Group.Value).ToLower() : "mọi vị trí");
            if (MinAge.HasValue && MaxAge.HasValue) parts.Add($"{MinAge}–{MaxAge} tuổi");
            else if (MaxAge.HasValue) parts.Add($"≤ {MaxAge} tuổi");
            else if (MinAge.HasValue) parts.Add($"≥ {MinAge} tuổi");
            var needNames = Needs.Where(n => n != "young").Select(n => CoachNeeds.Find(n)?.Name.ToLower()).Where(n => n != null).ToList();
            if (needNames.Count > 0)
                parts.Add("cần: " + string.Join(" + ", needNames));
            else if (Emphasis.Count > 0)
                parts.Add("ưu tiên " + string.Join(", ", Emphasis.Select(k => KnnService.FeatureList.First(f => f.Key == k).Name.ToLower())));
            parts.Add($"{K} gợi ý");
            return string.Join(" · ", parts);
        }
    }

    public interface IAiAssistantService
    {
        bool IsConfigured { get; }
        Task<AssistantResponse> AskAsync(string question, CancellationToken ct = default);
    }

    /// <summary>
    /// Trợ lý AI hỗ trợ tuyển chọn: (1) phân tích câu hỏi → yêu cầu tuyển chọn; (2) chạy KNN trên CSDL;
    /// (3) gửi câu hỏi + danh sách ứng viên cho mô hình ngôn ngữ để viết câu trả lời giải thích.
    /// Không có khóa API hoặc dịch vụ lỗi → trả lời tự động từ kết quả KNN.
    /// </summary>
    public class AiAssistantService : IAiAssistantService
    {
        private readonly IKnnService _knn;
        private readonly IAnalysisService _analysis;
        private readonly IStatsQaService _stats;
        private readonly AiDbTools _tools;
        private readonly IHttpClientFactory _http;
        private readonly AiAssistantOptions _opt;
        private readonly ILogger<AiAssistantService> _log;

        public AiAssistantService(IKnnService knn, IAnalysisService analysis, IStatsQaService stats, AiDbTools tools, IHttpClientFactory http,
            Microsoft.Extensions.Options.IOptions<AiAssistantOptions> opt, ILogger<AiAssistantService> log)
        {
            _knn = knn; _analysis = analysis; _stats = stats; _tools = tools; _http = http; _opt = opt.Value; _log = log;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.ApiKey);

        // Từ khóa (không dấu) → chiều đặc trưng được ưu tiên
        private static readonly (string Key, string[] Words)[] EmphasisWords =
        {
            ("pace", new[] { "toc do", "nhanh", "toc", "but toc" }),
            ("shooting", new[] { "dut diem", "sut", "chot ha" }),
            ("goals90", new[] { "ghi ban", "ban thang", "san ban", "ghi nhieu ban" }),
            ("passing", new[] { "chuyen bong", "chuyen", "phat dong", "kien thiet", "kien tao" }),
            ("dribbling", new[] { "re bong", "ky thuat", "dot pha", "qua nguoi" }),
            ("defending", new[] { "phong ngu", "tranh chap", "tac bong", "cat bong", "doc canh" }),
            ("physical", new[] { "the luc", "the chat", "suc manh", "khoe", "cao to", "to khoe", "khong chien", "cao lon" }),
            ("minshare", new[] { "phong do", "on dinh", "da chinh", "duoc tin dung", "thi dau nhieu", "nhieu phut", "kinh nghiem" }),
            ("csrate", new[] { "giu sach luoi", "sach luoi", "it thung luoi" }),
        };

        public ScoutIntent ParseIntent(string question, List<PlayerRow> players)
        {
            var s = " " + TextHelper.RemoveDiacritics(question).ToLowerInvariant() + " ";
            var it = new ScoutIntent();

            if (Regex.IsMatch(s, @"thu mon|thu thanh|\bgk\b")) it.Group = PositionGroup.GK;
            else if (Regex.IsMatch(s, @"hau ve|trung ve|hau ve canh|\bcb\b|\blb\b|\brb\b")) it.Group = PositionGroup.DEF;
            else if (Regex.IsMatch(s, @"tien dao|trung phong|tien ve canh|chay canh|cau thu canh|\bst\b|\blw\b|\brw\b|chan sut")) it.Group = PositionGroup.ATT;
            else if (Regex.IsMatch(s, @"tien ve|so 6|so 8|so 10|\bdm\b|\bcm\b|\bam\b")) it.Group = PositionGroup.MID;

            var m = Regex.Match(s, @"\bu ?(\d{2})\b");
            if (m.Success) it.MaxAge = int.Parse(m.Groups[1].Value);
            m = Regex.Match(s, @"(\d{2})\s*(?:-|–|den|toi)\s*(\d{2})\s*tuoi");
            if (m.Success) { it.MinAge = int.Parse(m.Groups[1].Value); it.MaxAge = int.Parse(m.Groups[2].Value); }
            m = Regex.Match(s, @"(?:duoi|nho hon|<)\s*(\d{2})");
            if (m.Success) it.MaxAge = int.Parse(m.Groups[1].Value) - 1;
            m = Regex.Match(s, @"(?:khong qua|toi da|<=|≤)\s*(\d{2})");
            if (m.Success) it.MaxAge = int.Parse(m.Groups[1].Value);
            m = Regex.Match(s, @"(?:tren|lon hon|>)\s*(\d{2})\s*tuoi");
            if (m.Success) it.MinAge = int.Parse(m.Groups[1].Value) + 1;
            if (it.MaxAge == null && Regex.IsMatch(s, @"\btre\b|tiem nang")) it.MaxAge = 23;

            m = Regex.Match(s, @"(?:top\s*(\d{1,2}))|(?:(\d{1,2})\s*(?:cau thu|ung vien|nguoi|goi y|lua chon))");
            if (m.Success) it.K = Math.Clamp(int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value), 1, 15);

            foreach (var (key, words) in EmphasisWords)
                if (words.Any(w => s.Contains(" " + w)) && !it.Emphasis.Contains(key)) it.Emphasis.Add(key);

            // Nhận đủ MỌI tiêu chí trong câu ("tốc độ nhanh và tì đè tốt" → tốc độ + tì đè), không chỉ tiêu chí đầu tiên
            var needs = CoachNeeds.DetectFromText(question);
            if (it.Group == null && needs.Any(n => n is "reflex" or "cleansheet")) it.Group = PositionGroup.GK;
            it.Needs = CoachNeeds.Valid(needs, it.Group);

            // "giống / tương tự / như <tên cầu thủ>": chọn cầu thủ có họ tên dài nhất xuất hiện trong câu
            if (Regex.IsMatch(s, @"giong|tuong tu|nhu|kieu|thay the"))
            {
                it.SimilarTo = players
                    .Select(r => (r, name: TextHelper.RemoveDiacritics(r.Player.FullName).ToLowerInvariant()))
                    .Where(x => s.Contains(" " + x.name))
                    .OrderByDescending(x => x.name.Length).Select(x => x.r).FirstOrDefault();
                if (it.SimilarTo != null && it.Group == null) it.Group = PositionGroups.Of(it.SimilarTo.Player.Position);
            }
            return it;
        }

        public async Task<AssistantResponse> AskAsync(string question, CancellationToken ct = default)
        {
            question = (question ?? "").Trim();
            if (question.Length > 500) question = question[..500];

            // (0) Có khóa API: mô hình ngôn ngữ tự chọn công cụ tra cứu CSDL (function calling) để trả lời mọi câu hỏi
            string? agentNotice = null;
            if (IsConfigured)
            {
                try { return await AgentAsync(question, ct); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Trợ lý AI (function calling) thất bại – chuyển sang chế độ tự động");
                    agentNotice = "Dịch vụ AI tạm thời không khả dụng – hiển thị câu trả lời tự động từ CSDL.";
                }
            }

            // (1) Câu hỏi tra cứu thống kê: trả lời trực tiếp từ CSDL
            var sa = await _stats.TryAnswerAsync(question);
            if (agentNotice != null)
            {
                var r = sa != null ? new AssistantResponse { Kind = "stats", Understood = sa.Understood, Answer = sa.Answer, Columns = sa.Columns, Rows = sa.Rows, Links = sa.Links }
                                   : await RuleBasedScoutAsync(question);
                r.Notice = agentNotice;
                return r;
            }
            if (sa != null) return await StatsResponseAsync(question, sa, ct);
            return await RuleBasedScoutAsync(question);
        }

        /// <summary>Chế độ tự động (không có LLM): trích yêu cầu tuyển chọn → KNN → trả lời theo mẫu.</summary>
        private async Task<AssistantResponse> RuleBasedScoutAsync(string question)
        {
            // (2) Câu hỏi tuyển chọn: trích yêu cầu → KNN
            var tid = await _analysis.DefaultTournamentIdAsync() ?? 0;
            var players = (await _analysis.LoadRowsAsync(tid)).Where(r => r.Stat != null).ToList();
            var intent = ParseIntent(question, players);
            if (intent.Group == null && intent.SimilarTo == null && intent.Emphasis.Count == 0 && intent.Needs.Count == 0 && intent.MaxAge == null && intent.MinAge == null)
            {
                // Không nhận ra yêu cầu → hướng dẫn cách hỏi, không gọi KNN / LLM
                return new AssistantResponse
                {
                    Kind = "help",
                    Understood = "chưa xác định được yêu cầu",
                    Answer = "Tôi có thể giúp hai việc:\n" +
                             "- **Tra cứu số liệu V.League**: “Ai ghi nhiều bàn nhất?”, “Cầu thủ nào thi đấu nhiều phút nhất?”, “Đội nào đang dẫn đầu?”, " +
                             "“Thủ môn nào giữ sạch lưới nhiều nhất?”, “Nguyễn Hoàng Đức ghi bao nhiêu bàn?”, “Kết quả vòng 6”, “So sánh Lucão và Milan Makarić”.\n" +
                             "- **Gợi ý tuyển chọn**: nêu **vị trí**, **độ tuổi**, **điểm mạnh** hoặc một **cầu thủ mẫu**, ví dụ “Tìm tiền đạo dưới 23 tuổi ghi bàn tốt”, “Tìm cầu thủ giống Nguyễn Lê Phát”."
                };
            }

            // Dựng truy vấn KNN từ yêu cầu
            var q = new KnnQuery { Group = intent.Group, TournamentId = tid, MinAge = intent.MinAge, MaxAge = intent.MaxAge, K = intent.K };
            q.Weights = KnnService.DefaultWeights(intent.Group);
            if (intent.SimilarTo != null)
            {
                q.Target = _knn.VectorOf(intent.SimilarTo);
                q.ExcludePlayerId = intent.SimilarTo.Player.Id;
            }
            else
            {
                q.Target = await _knn.SuggestTargetAsync(intent.Group, tid, q.MinMinutes);
            }
            var skillNeeds = intent.Needs.Where(n => n != "young").ToList();
            if (skillNeeds.Count > 0)
            {
                // Chỉ chấm theo đúng các tiêu chí HLV nêu, mỗi tiêu chí có trọng số ngang nhau →
                // cầu thủ phải tốt ở TẤT CẢ tiêu chí mới đứng đầu (giỏi một mặt mà yếu mặt kia sẽ bị đẩy xuống)
                q.Weights = KnnService.WeightsFromNeeds(skillNeeds);
                q.Filter = new NeedFilter { Needs = skillNeeds, MinAge = intent.MinAge, MaxAge = intent.MaxAge };
            }
            else foreach (var k in intent.Emphasis) q.Weights[k] = 3;

            var knn = await _knn.RecommendAsync(q);
            var res = new AssistantResponse
            {
                Understood = intent.Describe(),
                Candidates = knn.Neighbors.Select(n => new AssistantCandidate
                {
                    Id = n.Row.Player.Id,
                    Name = n.Row.Player.FullName,
                    Position = n.Row.Player.Position.ToString(),
                    Team = n.Row.Player.Team?.ShortName ?? "Tự do",
                    Age = n.Row.Player.Age,
                    Overall = n.Row.Player.Overall,
                    Match = n.Match,
                    Minutes = n.Row.Stat!.Minutes,
                    Goals = n.Row.Stat.Goals,
                    Starts = n.Row.Stat.Starts,
                    CleanSheets = n.Row.Stat.CleanSheets,
                    MinuteShare = Metrics.Find("minshare")!.Value(n.Row) is double ms ? Math.Round(ms, 1) : null,
                    Value = n.Row.Player.MarketValue,
                    Strengths = n.Strengths,
                    Gaps = n.Gaps
                }).ToList()
            };

            // Bảng kết quả: thêm một cột cho TỪNG tiêu chí đã nêu (VD Tốc độ km/h, Thể lực / tì đè) để HLV thấy rõ cầu thủ đạt cả hai
            var cols = CoachNeeds.Columns(skillNeeds);
            if (cols.Count > 0)
            {
                res.Columns = new List<string> { "#", "Cầu thủ", "Đội", "Tuổi" }.Concat(cols.Select(c => c.Header)).Append("Phù hợp").ToList();
                int i = 1;
                foreach (var n in knn.Neighbors)
                {
                    res.Rows.Add(new List<string> { (i++).ToString(), n.Row.Player.FullName, n.Row.Player.Team?.ShortName ?? "Tự do", n.Row.Player.Age.ToString() }
                        .Concat(cols.Select(c => c.Show(n.Row))).Append(n.Match.ToString("0.0", Fmt.VI) + "%").ToList());
                    res.Links.Add($"/Analysis/Player/{n.Row.Player.Id}");
                }
            }
            res.Answer = FallbackAnswer(intent, res.Candidates, knn.Neighbors.Select(n => cols.Select(c => $"{c.Header.ToLower()} {c.Show(n.Row)}").ToList()).ToList());
            return res;
        }

        private static Task<AssistantResponse> StatsResponseAsync(string question, StatsAnswer sa, CancellationToken ct)
            => Task.FromResult(new AssistantResponse { Kind = "stats", Understood = sa.Understood, Answer = sa.Answer, Columns = sa.Columns, Rows = sa.Rows, Links = sa.Links });

        private static string FallbackAnswer(ScoutIntent intent, List<AssistantCandidate> c, List<List<string>>? criteria = null)
        {
            if (c.Count == 0)
                return $"Không tìm thấy ứng viên phù hợp với yêu cầu ({intent.Describe()}). Hãy thử nới độ tuổi hoặc bỏ bớt điều kiện.";
            var sb = new StringBuilder();
            sb.AppendLine($"Với yêu cầu **{intent.Describe()}**, hệ thống đề xuất {c.Count} ứng viên phù hợp nhất:");
            foreach (var x in c.Take(3))
            {
                var idx = c.IndexOf(x);
                var crit = criteria != null && idx < criteria.Count && criteria[idx].Count > 0 ? string.Join(", ", criteria[idx]) : null;
                sb.Append($"- **{x.Name}** ({x.Position}, {x.Team}, {x.Age} tuổi) – phù hợp {x.Match.ToString("0.0", Fmt.VI)}%, " +
                    (crit ?? $"{x.Minutes} phút ({x.Starts} trận đá chính), " + (x.Position == "GK" ? $"{x.CleanSheets} trận sạch lưới" : $"{x.Goals} bàn")));
                if (x.Strengths.Count > 0) sb.Append($"; đạt yêu cầu về {string.Join(", ", x.Strengths).ToLower()}");
                if (x.Gaps.Count > 0) sb.Append($"; cần lưu ý {string.Join(", ", x.Gaps).ToLower()}");
                sb.AppendLine(".");
            }
            sb.Append("Nên mở trang phân tích chi tiết hoặc so sánh các ứng viên trước khi đưa vào danh sách theo dõi.");
            return sb.ToString();
        }

        // ==================================================================
        // Trợ lý AI có công cụ tra cứu CSDL (function calling)
        // ==================================================================
        private string AgentSystemPrompt() =>
            "Bạn là trợ lý dữ liệu và tuyển trạch bóng đá của hệ thống ScoutVN, hỗ trợ HLV và tuyển trạch viên Việt Nam. " +
            $"Hôm nay là {DateTime.Today:dd/MM/yyyy}. CSDL gồm V.League 1 mùa 2026/27 (đang đá, có số trận, phút, bàn, thẻ, sạch lưới) " +
            "và mùa 2025/26 (chỉ có bàn thắng, phạt đền, thẻ). " +
            "LUÔN gọi công cụ để lấy số liệu trước khi trả lời câu hỏi về cầu thủ, đội, trận đấu, bảng xếp hạng hay gợi ý tuyển chọn; " +
            "có thể gọi nhiều công cụ liên tiếp (VD tìm cầu thủ rồi xem hồ sơ chi tiết). " +
            "Tuyệt đối không bịa cầu thủ, đội, con số; nếu công cụ không có dữ liệu thì nói rõ. " +
            "Tốc độ km/h là giá trị quy đổi từ điểm tốc độ, giá trị cầu thủ là ước tính – nêu rõ khi dùng. " +
            "Trả lời bằng tiếng Việt tự nhiên, ngắn gọn (thường dưới 150 từ), in đậm tên cầu thủ và con số quan trọng bằng **…**, " +
            "dùng gạch đầu dòng khi liệt kê. Với câu hỏi tuyển chọn: nêu 2–3 cái tên đáng chú ý, lý do bằng số liệu và điểm cần lưu ý. " +
            "Câu hỏi ngoài bóng đá: lịch sự từ chối và gợi ý cách hỏi.";

        private record ToolCall(string Id, string Name, JsonObject Args);

        /// <summary>Giữ nguyên tiếng Việt trong JSON kết quả công cụ (ít token hơn \uXXXX).</summary>
        private static readonly JsonSerializerOptions ToolJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private async Task<AssistantResponse> AgentAsync(string question, CancellationToken ct)
        {
            var provider = (_opt.Provider ?? "gemini").ToLowerInvariant();
            var used = new List<string>();
            ToolResult? lastTable = null;
            if (provider is "anthropic") provider = "claude";
            var gemini = provider == "gemini";
            var claude = provider == "claude";

            // Lịch sử hội thoại theo định dạng của từng nhà cung cấp
            var messages = new JsonArray();
            if (gemini) messages.Add(new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = question }) });
            else if (claude) messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });   // system gửi riêng
            else
            {
                messages.Add(new JsonObject { ["role"] = "system", ["content"] = AgentSystemPrompt() });
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });
            }

            var rounds = Math.Clamp(_opt.MaxToolRounds, 1, 8);
            for (int round = 0; round <= rounds; round++)
            {
                var allowTools = round < rounds;   // vòng cuối: buộc trả lời bằng văn bản
                var body = await SendAsync(provider, messages, allowTools, ct);
                var (text, calls, assistantMsg) = gemini ? ParseGemini(body) : claude ? ParseClaude(body) : ParseOpenAi(body);
                if (calls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("LLM trả về nội dung rỗng");
                    var res = new AssistantResponse
                    {
                        Kind = "ai", Mode = "llm", Answer = text.Trim(),
                        Understood = used.Count == 0 ? "AI trả lời trực tiếp (không cần tra cứu)" : "AI tra cứu CSDL: " + string.Join(" → ", used)
                    };
                    if (lastTable != null && lastTable.Rows.Count > 0)
                    {
                        res.Columns = lastTable.Columns; res.Rows = lastTable.Rows; res.Links = lastTable.Links;
                    }
                    return res;
                }

                messages.Add(assistantMsg);
                var responses = new JsonArray();
                foreach (var c in calls.Take(4))
                {
                    var r = await _tools.ExecuteAsync(c.Name, c.Args);
                    used.Add(r.Summary);
                    if (r.Rows.Count > 0) lastTable = r;
                    if (claude)
                        responses.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = c.Id, ["content"] = r.Data.ToJsonString(ToolJson) });
                    else if (gemini)
                        responses.Add(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = c.Name, ["response"] = new JsonObject { ["result"] = r.Data.DeepClone() } } });
                    else
                        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = c.Id, ["content"] = r.Data.ToJsonString(ToolJson) });
                }
                if (gemini) messages.Add(new JsonObject { ["role"] = "user", ["parts"] = responses });
                if (claude) messages.Add(new JsonObject { ["role"] = "user", ["content"] = responses });
            }
            throw new InvalidOperationException("Vượt quá số vòng gọi công cụ");
        }

        private static JsonArray ToolDeclarations(bool gemini, bool claude = false)
        {
            var arr = new JsonArray();
            foreach (var (name, desc, parameters) in AiDbTools.Definitions)
            {
                if (claude) { arr.Add(new JsonObject { ["name"] = name, ["description"] = desc, ["input_schema"] = JsonNode.Parse(parameters) }); continue; }
                var fn = new JsonObject { ["name"] = name, ["description"] = desc, ["parameters"] = JsonNode.Parse(parameters) };
                arr.Add(gemini ? fn : new JsonObject { ["type"] = "function", ["function"] = fn });
            }
            return arr;
        }

        private async Task<string> SendAsync(string provider, JsonArray messages, bool allowTools, CancellationToken ct)
        {
            var client = _http.CreateClient("llm");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_opt.TimeoutSeconds, 5, 120));
            HttpRequestMessage req;
            JsonObject payload;
            if (provider == "gemini")
            {
                var model = ModelFor("gemini", "gemini-2.5-flash");
                var baseUrl = string.IsNullOrWhiteSpace(_opt.BaseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : _opt.BaseUrl.TrimEnd('/');
                req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{model}:generateContent");
                req.Headers.Add("x-goog-api-key", _opt.ApiKey);
                payload = new JsonObject
                {
                    ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = AgentSystemPrompt() }) },
                    ["contents"] = messages.DeepClone(),
                    ["generationConfig"] = new JsonObject { ["temperature"] = 0.2 }
                };
                if (allowTools) payload["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = ToolDeclarations(true) });
            }
            else if (provider == "claude")
            {
                // Anthropic Messages API: system riêng, công cụ dùng input_schema, kết quả công cụ là khối tool_result
                var model = ModelFor("claude", "claude-sonnet-5-5");
                var baseUrl = string.IsNullOrWhiteSpace(_opt.BaseUrl) ? "https://api.anthropic.com/v1" : _opt.BaseUrl.TrimEnd('/');
                req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/messages");
                req.Headers.Add("x-api-key", _opt.ApiKey);
                req.Headers.Add("anthropic-version", string.IsNullOrWhiteSpace(_opt.AnthropicVersion) ? "2023-06-01" : _opt.AnthropicVersion);
                payload = new JsonObject
                {
                    ["model"] = model, ["max_tokens"] = 1500, ["temperature"] = 0.2,
                    ["system"] = AgentSystemPrompt(), ["messages"] = messages.DeepClone(),
                    // Lịch sử có khối tool_use nên luôn gửi khai báo công cụ; vòng cuối cấm gọi thêm (tool_choice = none)
                    ["tools"] = ToolDeclarations(false, claude: true),
                    ["tool_choice"] = new JsonObject { ["type"] = allowTools ? "auto" : "none" }
                };
            }
            else
            {
                var model = ModelFor("openai", "gpt-4o-mini");
                var baseUrl = string.IsNullOrWhiteSpace(_opt.BaseUrl) ? "https://api.openai.com/v1" : _opt.BaseUrl.TrimEnd('/');
                req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
                payload = new JsonObject { ["model"] = model, ["temperature"] = 0.2, ["messages"] = messages.DeepClone() };
                if (allowTools) { payload["tools"] = ToolDeclarations(false); payload["tool_choice"] = "auto"; }
            }
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"LLM HTTP {(int)resp.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
            return body;
        }

        private static (string? Text, List<ToolCall> Calls, JsonNode AssistantMsg) ParseOpenAi(string body)
        {
            var root = JsonNode.Parse(body)!;
            var msg = root["choices"]![0]!["message"]!.AsObject();
            var calls = new List<ToolCall>();
            if (msg["tool_calls"] is JsonArray tc)
                foreach (var c in tc)
                {
                    var fn = c!["function"]!;
                    JsonObject args;
                    try { args = JsonNode.Parse(fn["arguments"]?.GetValue<string>() ?? "{}") as JsonObject ?? new(); } catch { args = new(); }
                    calls.Add(new ToolCall(c["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"), fn["name"]!.GetValue<string>(), args));
                }
            var text = msg["content"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : null;
            return (text, calls, msg.DeepClone());
        }

        /// <summary>Tên model theo nhà cung cấp; nếu cấu hình để trống hoặc còn tên model của nhà cung cấp khác (VD đổi Provider
        /// sang "claude" nhưng Model vẫn là "gemini-2.5-flash") thì dùng model mặc định.</summary>
        private string ModelFor(string provider, string fallback)
        {
            var m = _opt.Model?.Trim();
            if (string.IsNullOrEmpty(m)) return fallback;
            var isGemini = m.StartsWith("gemini", StringComparison.OrdinalIgnoreCase);
            var isClaude = m.StartsWith("claude", StringComparison.OrdinalIgnoreCase);
            return provider switch
            {
                "claude" => isClaude ? m : fallback,
                "gemini" => isGemini ? m : fallback,
                _ => isGemini || isClaude ? fallback : m
            };
        }

        private static (string? Text, List<ToolCall> Calls, JsonNode AssistantMsg) ParseClaude(string body)
        {
            var root = JsonNode.Parse(body)!;
            var content = root["content"] as JsonArray ?? new JsonArray();
            var calls = new List<ToolCall>();
            var sb = new StringBuilder();
            foreach (var b in content)
            {
                var type = b?["type"]?.GetValue<string>();
                if (type == "tool_use")
                    calls.Add(new ToolCall(b!["id"]!.GetValue<string>(), b["name"]!.GetValue<string>(), b["input"]?.DeepClone() as JsonObject ?? new()));
                else if (type == "text") sb.Append(b!["text"]?.GetValue<string>());
            }
            // Gửi lại nguyên các khối của trợ lý (kể cả tool_use) ở lượt sau
            var msg = new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() };
            return (sb.ToString(), calls, msg);
        }

        private static (string? Text, List<ToolCall> Calls, JsonNode AssistantMsg) ParseGemini(string body)
        {
            var root = JsonNode.Parse(body)!;
            var content = root["candidates"]![0]!["content"]!.AsObject();
            var calls = new List<ToolCall>();
            var sb = new StringBuilder();
            if (content["parts"] is JsonArray parts)
                foreach (var p in parts)
                {
                    if (p?["functionCall"] is JsonObject fc)
                        calls.Add(new ToolCall(Guid.NewGuid().ToString("N"), fc["name"]!.GetValue<string>(), fc["args"]?.DeepClone() as JsonObject ?? new()));
                    else if (p?["text"] is JsonValue tv && tv.TryGetValue<string>(out var t)) sb.Append(t);
                }
            var msg = content.DeepClone().AsObject();
            msg["role"] = "model";
            return (sb.ToString(), calls, msg);
        }

        private static StringContent JsonContent(object o)
            => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    }
}
