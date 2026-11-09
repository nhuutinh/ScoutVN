using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    /// <summary>Đội bóng / Câu lạc bộ.</summary>
    public class Team
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên đội bóng")]
        [StringLength(100)]
        [Display(Name = "Tên đội bóng")]
        public string Name { get; set; } = string.Empty;

        [StringLength(10)]
        [Display(Name = "Tên viết tắt")]
        public string? ShortName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tỉnh/thành phố")]
        [StringLength(100)]
        [Display(Name = "Tỉnh / Thành phố")]
        public string City { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Sân nhà")]
        public string? Stadium { get; set; }

        [Range(1850, 2100, ErrorMessage = "Năm thành lập không hợp lệ")]
        [Display(Name = "Năm thành lập")]
        public int? FoundedYear { get; set; }

        [StringLength(100)]
        [Display(Name = "Huấn luyện viên trưởng")]
        public string? HeadCoach { get; set; }

        [Url(ErrorMessage = "Đường dẫn không hợp lệ")]
        [StringLength(300)]
        [Display(Name = "Logo (URL)")]
        public string? LogoUrl { get; set; }

        [StringLength(1000)]
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        [Display(Name = "Mã nguồn (FotMob)")]
        public int? SourceId { get; set; }

        public ICollection<Player> Players { get; set; } = new List<Player>();
    }
}
