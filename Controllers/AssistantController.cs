using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>Tuần 7: trợ lý AI hỗ trợ tuyển chọn (hỏi đáp bằng tiếng Việt, kết hợp KNN + mô hình ngôn ngữ).</summary>
    [Authorize]
    public class AssistantController : Controller
    {
        private readonly IAiAssistantService _ai;
        public AssistantController(IAiAssistantService ai) => _ai = ai;

        public IActionResult Index()
        {
            ViewBag.Configured = _ai.IsConfigured;
            return View();
        }

        // POST /Assistant/Ask  (JSON)  – trả về câu trả lời + danh sách ứng viên
        [HttpPost, ValidateAntiForgeryToken, Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
        public async Task<IActionResult> Ask([FromBody] AssistantRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req?.Question))
                return BadRequest(new { error = "Hãy nhập câu hỏi." });
            return Json(await _ai.AskAsync(req.Question, ct));
        }
    }
}
