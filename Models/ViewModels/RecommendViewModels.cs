using Microsoft.AspNetCore.Mvc.Rendering;
using ScoutVN.Services;

namespace ScoutVN.Models.ViewModels
{
    public class RecommendViewModel
    {
        /// <summary>"criteria" = theo tiêu chí mong muốn; "similar" = tìm cầu thủ tương tự.</summary>
        public string Mode { get; set; } = "criteria";
        public PositionGroup? Group { get; set; }
        public int TournamentId { get; set; }
        public string ScopeName { get; set; } = "";
        public List<SelectListItem> Tournaments { get; set; } = new();
        public int? MinAge { get; set; }
        public int? MaxAge { get; set; }
        public int MinMinutes { get; set; } = KnnQuery.DefaultMinMinutes;
        public int K { get; set; } = 10;
        public Dictionary<string, double> Target { get; set; } = new();
        public Dictionary<string, double> Weights { get; set; } = new();
        public int? PlayerId { get; set; }
        public List<SelectListItem> PlayerOptions { get; set; } = new();
        public PlayerRow? Sample { get; set; }
        public IReadOnlyList<KnnFeature> Features { get; set; } = Array.Empty<KnnFeature>();
        public KnnResult? Result { get; set; }
        /// <summary>Nhu cầu chuyên môn HLV chọn (tốc độ, tì đè, không chiến...).</summary>
        public List<string> Needs { get; set; } = new();
        /// <summary>Người dùng bấm tìm nhưng chưa chọn nhu cầu (tiêu chí) nào.</summary>
        public bool NeedsMissing { get; set; }
        public NeedFilter Filter { get; set; } = new();
        public List<NeedColumn> Columns { get; set; } = new();
        /// <summary>Danh sách cầu thủ cho ô tìm kiếm (id, nhãn).</summary>
        public List<PlayerPick> Picks { get; set; } = new();
    }

    /// <summary>Một mục trong ô nhập tìm cầu thủ (gõ tên để lọc).</summary>
    public record PlayerPick(int Id, string Name, string Info);

    // ===== Trợ lý AI =====
    public class AssistantRequest
    {
        public string Question { get; set; } = "";
    }

    public class AssistantCandidate
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Position { get; set; } = "";
        public string Team { get; set; } = "";
        public int Age { get; set; }
        public int Overall { get; set; }
        public double Match { get; set; }
        public int Minutes { get; set; }
        public int Goals { get; set; }
        public int Starts { get; set; }
        public int CleanSheets { get; set; }
        public double? MinuteShare { get; set; }
        /// <summary>Giá trị chuyển nhượng (triệu VNĐ), null nếu nguồn không có.</summary>
        public int? Value { get; set; }
        public List<string> Strengths { get; set; } = new();
        public List<string> Gaps { get; set; } = new();
    }

    public class AssistantResponse
    {
        public string Answer { get; set; } = "";
        /// <summary>"llm" = trả lời bởi mô hình ngôn ngữ; "fallback" = trả lời tự động từ kết quả KNN.</summary>
        public string Mode { get; set; } = "fallback";
        public string? Notice { get; set; }
        /// <summary>Tóm tắt yêu cầu mà hệ thống hiểu được (để người dùng kiểm tra).</summary>
        public string Understood { get; set; } = "";
        public List<AssistantCandidate> Candidates { get; set; } = new();
        /// <summary>"scout" = gợi ý tuyển chọn (KNN); "stats" = tra cứu thống kê; "help" = hướng dẫn.</summary>
        public string Kind { get; set; } = "scout";
        /// <summary>Bảng số liệu của câu trả lời tra cứu.</summary>
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public List<string?> Links { get; set; } = new();
    }
}
