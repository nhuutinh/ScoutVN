using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    public enum NewsCategory
    {
        [Display(Name = "Tin hệ thống")] System = 1,
        [Display(Name = "Giải đấu")] Tournament = 2,
        [Display(Name = "Phân tích")] Analysis = 3,
        [Display(Name = "Chuyển nhượng")] Transfer = 4,
        [Display(Name = "Hướng dẫn")] Guide = 5
    }

    /// <summary>Bài viết tin tức hiển thị trên cổng thông tin công khai.</summary>
    public class News
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tiêu đề")]
        [StringLength(200)]
        [Display(Name = "Tiêu đề")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập tóm tắt")]
        [StringLength(500)]
        [Display(Name = "Tóm tắt")]
        public string Summary { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập nội dung")]
        [Display(Name = "Nội dung")]
        [DataType(DataType.MultilineText)]
        public string Content { get; set; } = string.Empty;

        [Url(ErrorMessage = "Đường dẫn ảnh không hợp lệ")]
        [StringLength(500)]
        [Display(Name = "Ảnh đại diện (URL)")]
        public string? ImageUrl { get; set; }

        [Display(Name = "Chuyên mục")]
        public NewsCategory Category { get; set; } = NewsCategory.Tournament;

        [Display(Name = "Giải đấu liên quan")]
        public int? TournamentId { get; set; }
        public Tournament? Tournament { get; set; }

        [Display(Name = "Ghim nổi bật")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Đã xuất bản")]
        public bool IsPublished { get; set; } = true;

        [Display(Name = "Ngày đăng")]
        public DateTime PublishedAt { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "Tác giả")]
        public string? AuthorName { get; set; }

        [Display(Name = "Lượt xem")]
        public int ViewCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
