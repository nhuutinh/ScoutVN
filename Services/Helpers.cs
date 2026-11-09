using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ScoutVN.Services
{
    public static class EnumExtensions
    {
        /// <summary>Lấy tên hiển thị tiếng Việt từ [Display(Name=...)].</summary>
        public static string DisplayName(this Enum value)
        {
            var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
        }

        public static List<SelectListItem> ToSelectList<TEnum>(TEnum? selected = null) where TEnum : struct, Enum
            => Enum.GetValues<TEnum>()
                .Select(e => new SelectListItem(e.DisplayName(), e.ToString(), selected.HasValue && selected.Value.Equals(e)))
                .ToList();
    }

    public static class TextHelper
    {
        /// <summary>Bỏ dấu tiếng Việt, dùng cho so khớp không phân biệt dấu (vd: tìm "nguyen" ra "Nguyễn").</summary>
        public static string RemoveDiacritics(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
        }
    }

    public static class RatingHelper
    {
        /// <summary>Màu Bootstrap cho ô chỉ số.</summary>
        public static string BadgeClass(int value) => value switch
        {
            >= 80 => "bg-success",
            >= 70 => "bg-primary",
            >= 60 => "bg-info text-dark",
            >= 50 => "bg-warning text-dark",
            _ => "bg-secondary"
        };
    }
}

namespace ScoutVN.Services
{
    /// <summary>Định dạng số kiểu Việt Nam (dấu chấm phân cách hàng nghìn: 1.500).</summary>
    public static class Fmt
    {
        public static readonly System.Globalization.CultureInfo VI = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");

        /// <summary>Hiển thị giá trị lưu theo triệu VNĐ: ≥ 1 tỷ → "11,4 tỷ VNĐ", nhỏ hơn → "850 triệu VNĐ".</summary>
        public static string Money(int? trieu, bool unit = true)
        {
            if (!trieu.HasValue) return "–";
            var s = trieu.Value >= 1000 ? (trieu.Value / 1000.0).ToString("0.#", VI) + " tỷ" : trieu.Value.ToString("N0", VI) + " triệu";
            return unit ? s + " VNĐ" : s;
        }
    }

    public static class PortalHelper
    {
        /// <summary>Tạo đường dẫn thân thiện từ tiêu đề: "Ra mắt cổng thông tin" → "ra-mat-cong-thong-tin".</summary>
        public static string Slug(string? text)
        {
            var s = TextHelper.RemoveDiacritics(text).ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            var r = sb.ToString().Trim('-');
            return r.Length > 80 ? r[..80].TrimEnd('-') : r;
        }

        /// <summary>Tách nội dung thành các đoạn (dòng trống là ranh giới đoạn).</summary>
        public static IEnumerable<string> Paragraphs(string? content)
            => (content ?? "").Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static string TimeAgo(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return "vừa xong";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} phút trước";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours} giờ trước";
            if (d.TotalDays < 7) return $"{(int)d.TotalDays} ngày trước";
            return t.ToString("dd/MM/yyyy");
        }

        public static string CategoryIcon(ScoutVN.Models.NewsCategory c) => c switch
        {
            ScoutVN.Models.NewsCategory.System => "bi-megaphone",
            ScoutVN.Models.NewsCategory.Tournament => "bi-trophy",
            ScoutVN.Models.NewsCategory.Analysis => "bi-graph-up-arrow",
            ScoutVN.Models.NewsCategory.Transfer => "bi-arrow-left-right",
            _ => "bi-journal-text"
        };

        public static string Initials(string? shortName, string name)
            => !string.IsNullOrWhiteSpace(shortName) ? shortName!
               : string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(w => char.ToUpper(w[0])));
    }
}
