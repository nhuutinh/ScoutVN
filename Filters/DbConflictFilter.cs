using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace ScoutVN.Filters
{
    /// <summary>
    /// Nhiều người nhập liệu cùng lúc: nếu hai người lưu trùng (VD cùng thêm thống kê một cầu thủ cho một giải,
    /// vi phạm ràng buộc duy nhất) hoặc bản ghi vừa bị người khác xóa, CSDL báo lỗi DbUpdateException.
    /// Thay vì trang lỗi 500, quay lại trang trước kèm thông báo dễ hiểu để người dùng thử lại.
    /// </summary>
    public class DbConflictFilter : IExceptionFilter
    {
        private readonly ITempDataDictionaryFactory _tempData;
        private readonly ILogger<DbConflictFilter> _log;
        public DbConflictFilter(ITempDataDictionaryFactory tempData, ILogger<DbConflictFilter> log) { _tempData = tempData; _log = log; }

        public void OnException(ExceptionContext context)
        {
            if (context.Exception is not DbUpdateException ex) return;
            var http = context.HttpContext;
            _log.LogWarning(ex, "Xung đột khi lưu dữ liệu tại {Path}", http.Request.Path);
            var msg = ex is DbUpdateConcurrencyException
                ? "Bản ghi vừa được người khác sửa hoặc xóa. Vui lòng tải lại trang và thử lại."
                : "Không lưu được vì dữ liệu vừa bị trùng hoặc thay đổi bởi người dùng khác. Vui lòng kiểm tra lại và thử lần nữa.";
            if (http.Request.Headers.Accept.ToString().Contains("application/json"))
            {
                context.Result = new ObjectResult(new { error = msg }) { StatusCode = StatusCodes.Status409Conflict };
            }
            else
            {
                _tempData.GetTempData(http)["Error"] = msg;
                var back = http.Request.Headers.Referer.ToString();
                var local = Uri.TryCreate(back, UriKind.Absolute, out var u) && u.Host == http.Request.Host.Host ? u.PathAndQuery : "/";
                context.Result = new RedirectResult(local);
            }
            context.ExceptionHandled = true;
        }
    }
}
