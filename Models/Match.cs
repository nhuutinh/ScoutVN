using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    /// <summary>Một trận đấu trong giải: dùng để tính bảng xếp hạng đội và số trận đội đã đá (tỉ lệ thời gian thi đấu của cầu thủ).</summary>
    public class Match : IValidatableObject
    {
        public int Id { get; set; }

        [Display(Name = "Giải đấu")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn giải đấu")]
        public int TournamentId { get; set; }
        public Tournament? Tournament { get; set; }

        [Range(1, 60)] [Display(Name = "Vòng")] public int Round { get; set; }

        [Display(Name = "Thời gian")]
        public DateTime KickOff { get; set; }

        [Display(Name = "Đội nhà")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đội nhà")]
        public int HomeTeamId { get; set; }
        public Team? HomeTeam { get; set; }

        [Display(Name = "Đội khách")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đội khách")]
        public int AwayTeamId { get; set; }
        public Team? AwayTeam { get; set; }

        /// <summary>null = trận chưa diễn ra.</summary>
        [Range(0, 30)] [Display(Name = "Bàn đội nhà")] public int? HomeGoals { get; set; }
        [Range(0, 30)] [Display(Name = "Bàn đội khách")] public int? AwayGoals { get; set; }

        [Display(Name = "Mã nguồn (FotMob)")]
        public int? SourceId { get; set; }

        public bool IsPlayed => HomeGoals.HasValue && AwayGoals.HasValue;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (HomeTeamId == AwayTeamId)
                yield return new ValidationResult("Đội nhà và đội khách phải khác nhau", new[] { nameof(AwayTeamId) });
            if (HomeGoals.HasValue != AwayGoals.HasValue)
                yield return new ValidationResult("Nhập đủ tỉ số của cả hai đội (hoặc để trống cả hai)", new[] { nameof(AwayGoals) });
        }
    }
}
