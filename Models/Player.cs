using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScoutVN.Models
{
    /// <summary>Hồ sơ cầu thủ, gồm thông tin cá nhân và bộ chỉ số năng lực (thang 1–100).
    /// Bộ chỉ số năng lực là một phần đầu vào của thuật toán KNN.</summary>
    public class Player : IValidatableObject
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập họ tên cầu thủ")]
        [StringLength(100)]
        [Display(Name = "Họ và tên")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập ngày sinh")]
        [DataType(DataType.Date)]
        [Display(Name = "Ngày sinh")]
        public DateTime DateOfBirth { get; set; } = new DateTime(2000, 1, 1);

        [Required, StringLength(60)]
        [Display(Name = "Quốc tịch")]
        public string Nationality { get; set; } = "Việt Nam";

        [StringLength(100)]
        [Display(Name = "Quê quán")]
        public string? Hometown { get; set; }

        [Display(Name = "Vị trí")]
        public Position Position { get; set; } = Position.CM;

        [Display(Name = "Chân thuận")]
        public PreferredFoot PreferredFoot { get; set; } = PreferredFoot.Right;

        [Range(140, 220, ErrorMessage = "Chiều cao từ 140 đến 220 cm")]
        [Display(Name = "Chiều cao (cm)")]
        public int? HeightCm { get; set; }

        [Range(40, 120, ErrorMessage = "Cân nặng từ 40 đến 120 kg")]
        [Display(Name = "Cân nặng (kg)")]
        public int? WeightKg { get; set; }

        [Range(1, 99, ErrorMessage = "Số áo từ 1 đến 99")]
        [Display(Name = "Số áo")]
        public int? ShirtNumber { get; set; }

        [Display(Name = "Đội bóng")]
        public int? TeamId { get; set; }
        public Team? Team { get; set; }

        [Display(Name = "Trạng thái")]
        public PlayerStatus Status { get; set; } = PlayerStatus.Active;

        [Range(0, 1_000_000, ErrorMessage = "Giá trị không hợp lệ")]
        [Display(Name = "Giá trị ước tính (triệu VNĐ, VD 11400 = 11,4 tỷ)")]
        public int? MarketValue { get; set; }

        [Url(ErrorMessage = "Đường dẫn không hợp lệ")]
        [StringLength(300)]
        [Display(Name = "Ảnh (URL)")]
        public string? PhotoUrl { get; set; }

        // ===== Chỉ số năng lực (1 - 100) =====
        [Range(1, 100)] [Display(Name = "Tốc độ")] public int Pace { get; set; } = 50;
        [Range(1, 100)] [Display(Name = "Dứt điểm")] public int Shooting { get; set; } = 50;
        [Range(1, 100)] [Display(Name = "Chuyền bóng")] public int Passing { get; set; } = 50;
        [Range(1, 100)] [Display(Name = "Rê bóng")] public int Dribbling { get; set; } = 50;
        [Range(1, 100)] [Display(Name = "Phòng ngự")] public int Defending { get; set; } = 50;
        [Range(1, 100)] [Display(Name = "Thể lực")] public int Physical { get; set; } = 50;

        /// <summary>Mã cầu thủ ở nguồn dữ liệu (FotMob) – phục vụ đối chiếu, cập nhật lại dữ liệu.</summary>
        [Display(Name = "Mã nguồn (FotMob)")]
        public int? SourceId { get; set; }

        /// <summary>true: 6 chỉ số năng lực do hệ thống ước lượng từ dữ liệu thật (chưa được HLV đánh giá lại).</summary>
        [Display(Name = "Chỉ số ước tính")]
        public bool AttributesEstimated { get; set; }

        [StringLength(1000)]
        [Display(Name = "Ghi chú")]
        public string? Notes { get; set; }

        [Display(Name = "Ngày cập nhật")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public ICollection<PlayerStat> Stats { get; set; } = new List<PlayerStat>();

        // ===== Thuộc tính tính toán (không lưu CSDL) =====
        [NotMapped]
        [Display(Name = "Tuổi")]
        public int Age
        {
            get
            {
                var today = DateTime.Today;
                var age = today.Year - DateOfBirth.Year;
                if (DateOfBirth.Date > today.AddYears(-age)) age--;
                return age;
            }
        }

                /// <summary>Chỉ số tổng (OVR) – được lưu xuống CSDL để có thể sắp xếp/lọc bằng SQL.
        /// Giá trị luôn tính lại từ các chỉ số năng lực, setter rỗng để EF Core ánh xạ cột.</summary>
        [Display(Name = "Chỉ số tổng")]
        public int Overall
        {
            get => PlayerRating.Overall(this);
            private set { }
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (DateOfBirth > DateTime.Today)
                yield return new ValidationResult("Ngày sinh không được ở tương lai", new[] { nameof(DateOfBirth) });
            else if (Age < 14 || Age > 45)
                yield return new ValidationResult("Tuổi cầu thủ phải từ 14 đến 45", new[] { nameof(DateOfBirth) });
        }
    }

    /// <summary>Công thức tính chỉ số tổng theo vị trí. Dùng chung cho danh sách, sắp xếp, phân tích.</summary>
    public static class PlayerRating
    {
        public static int Overall(Player p) => Overall(p.Position, p.Pace, p.Shooting, p.Passing, p.Dribbling, p.Defending, p.Physical);

        public static int Overall(Position pos, int pac, int sho, int pas, int dri, int def, int phy)
        {
            // Trọng số: tốc độ, dứt điểm, chuyền, rê, phòng ngự, thể lực
            double[] w = pos switch
            {
                Position.GK => new[] { 0.10, 0.00, 0.20, 0.05, 0.40, 0.25 },
                Position.CB => new[] { 0.10, 0.00, 0.10, 0.05, 0.45, 0.30 },
                Position.LB or Position.RB => new[] { 0.25, 0.00, 0.15, 0.10, 0.30, 0.20 },
                Position.DM => new[] { 0.05, 0.05, 0.25, 0.10, 0.35, 0.20 },
                Position.CM => new[] { 0.10, 0.10, 0.30, 0.20, 0.15, 0.15 },
                Position.AM => new[] { 0.10, 0.20, 0.30, 0.30, 0.00, 0.10 },
                Position.LW or Position.RW => new[] { 0.30, 0.20, 0.15, 0.30, 0.00, 0.05 },
                _ => new[] { 0.20, 0.40, 0.05, 0.20, 0.00, 0.15 } // ST
            };
            var v = w[0] * pac + w[1] * sho + w[2] * pas + w[3] * dri + w[4] * def + w[5] * phy;
            return (int)Math.Round(v);
        }
    }
}
