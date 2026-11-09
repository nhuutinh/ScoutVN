using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    /// <summary>Giải đấu theo từng mùa giải.</summary>
    public class Tournament : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên giải đấu")]
        [StringLength(150)]
        [Display(Name = "Tên giải đấu")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mùa giải")]
        [RegularExpression(@"^\d{4}(/\d{2,4})?$", ErrorMessage = "Mùa giải có dạng 2025/26 hoặc 2026")]
        [StringLength(20)]
        [Display(Name = "Mùa giải")]
        public string Season { get; set; } = string.Empty;

        [Display(Name = "Loại giải")]
        public TournamentType Type { get; set; } = TournamentType.League;

        [StringLength(150)]
        [Display(Name = "Đơn vị tổ chức")]
        public string? Organizer { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Ngày bắt đầu")]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Ngày kết thúc")]
        public DateTime? EndDate { get; set; }

        [StringLength(1000)]
        [Display(Name = "Mô tả")]
        public string? Description { get; set; }

        /// <summary>true: có số liệu ra sân theo trận (số trận, đá chính, số phút); false: chỉ có bàn thắng và thẻ phạt.</summary>
        [Display(Name = "Có số liệu ra sân chi tiết")]
        public bool HasMatchDetails { get; set; } = true;

        public ICollection<PlayerStat> PlayerStats { get; set; } = new List<PlayerStat>();
        public ICollection<Match> Matches { get; set; } = new List<Match>();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartDate.HasValue && EndDate.HasValue && EndDate < StartDate)
                yield return new ValidationResult("Ngày kết thúc phải sau ngày bắt đầu", new[] { nameof(EndDate) });
        }
    }
}
