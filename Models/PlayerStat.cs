using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScoutVN.Models
{
    /// <summary>Thống kê thi đấu của một cầu thủ trong một giải đấu (mỗi cặp Cầu thủ – Giải đấu là duy nhất).
    /// Chỉ gồm các chỉ số có trong nguồn dữ liệu công khai của V.League (FotMob).</summary>
    public class PlayerStat : IValidatableObject
    {
        public int Id { get; set; }

        [Display(Name = "Cầu thủ")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn cầu thủ")]
        public int PlayerId { get; set; }
        public Player? Player { get; set; }

        [Display(Name = "Giải đấu")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn giải đấu")]
        public int TournamentId { get; set; }
        public Tournament? Tournament { get; set; }

        /// <summary>Đội cầu thủ khoác áo trong giải này (có thể khác đội hiện tại nếu đã chuyển nhượng).</summary>
        [Display(Name = "Đội thi đấu")]
        public int? TeamId { get; set; }
        public Team? Team { get; set; }

        [Range(0, 100)] [Display(Name = "Số trận")] public int Appearances { get; set; }
        [Range(0, 100)] [Display(Name = "Đá chính")] public int Starts { get; set; }
        [Range(0, 9000)] [Display(Name = "Số phút")] public int MinutesPlayed { get; set; }
        [Range(0, 200)] [Display(Name = "Bàn thắng")] public int Goals { get; set; }
        /// <summary>null = nguồn không có số liệu phạt đền (mùa 2026/27).</summary>
        [Range(0, 100)] [Display(Name = "Bàn phạt đền")] public int? PenaltyGoals { get; set; }
        [Range(0, 100)] [Display(Name = "Giữ sạch lưới")] public int CleanSheets { get; set; }
        [Range(0, 300)] [Display(Name = "Bàn thua (thủ môn)")] public int GoalsConceded { get; set; }
        [Range(0, 100)] [Display(Name = "Thẻ vàng")] public int YellowCards { get; set; }
        [Range(0, 50)] [Display(Name = "Thẻ đỏ")] public int RedCards { get; set; }

        [NotMapped]
        [Display(Name = "Bàn / 90 phút")]
        public double? GoalsPer90 => MinutesPlayed > 0 ? Math.Round(Goals * 90.0 / MinutesPlayed, 2) : null;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MinutesPlayed > Appearances * 120)
                yield return new ValidationResult("Số phút vượt quá số trận × 120", new[] { nameof(MinutesPlayed) });
            if (Starts > Appearances)
                yield return new ValidationResult("Số trận đá chính không được lớn hơn số trận", new[] { nameof(Starts) });
            if (PenaltyGoals > Goals)
                yield return new ValidationResult("Số bàn phạt đền không được lớn hơn tổng số bàn", new[] { nameof(PenaltyGoals) });
            if (CleanSheets > Appearances)
                yield return new ValidationResult("Số trận giữ sạch lưới không được lớn hơn số trận", new[] { nameof(CleanSheets) });
        }
    }
}
