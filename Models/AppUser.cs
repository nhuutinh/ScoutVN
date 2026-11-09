using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    /// <summary>Tài khoản người dùng (Admin hoặc HLV/Tuyển trạch viên).</summary>
    public class AppUser
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [EmailAddress, StringLength(150)]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Display(Name = "Vai trò")]
        public UserRole Role { get; set; } = UserRole.Scout;

        [Display(Name = "Hoạt động")]
        public bool IsActive { get; set; } = true;

        /// <summary>Tài khoản tự đăng ký phải được quản trị viên phê duyệt mới đăng nhập được.</summary>
        [Display(Name = "Đã phê duyệt")]
        public bool IsApproved { get; set; } = true;

        [Display(Name = "Ngày tạo")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
