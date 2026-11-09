using System.ComponentModel.DataAnnotations;

namespace ScoutVN.Models
{
    /// <summary>Vai trò người dùng trong hệ thống.</summary>
    public enum UserRole
    {
        [Display(Name = "Quản trị viên")] Admin = 1,
        [Display(Name = "HLV / Tuyển trạch viên")] Scout = 2
    }

    /// <summary>Vị trí thi đấu chính của cầu thủ.</summary>
    public enum Position
    {
        [Display(Name = "Thủ môn (GK)")] GK = 1,
        [Display(Name = "Trung vệ (CB)")] CB = 2,
        [Display(Name = "Hậu vệ trái (LB)")] LB = 3,
        [Display(Name = "Hậu vệ phải (RB)")] RB = 4,
        [Display(Name = "Tiền vệ phòng ngự (DM)")] DM = 5,
        [Display(Name = "Tiền vệ trung tâm (CM)")] CM = 6,
        [Display(Name = "Tiền vệ tấn công (AM)")] AM = 7,
        [Display(Name = "Tiền vệ cánh trái (LW)")] LW = 8,
        [Display(Name = "Tiền vệ cánh phải (RW)")] RW = 9,
        [Display(Name = "Tiền đạo (ST)")] ST = 10
    }

    /// <summary>Chân thuận.</summary>
    public enum PreferredFoot
    {
        [Display(Name = "Chưa rõ")] Unknown = 0,
        [Display(Name = "Phải")] Right = 1,
        [Display(Name = "Trái")] Left = 2,
        [Display(Name = "Cả hai")] Both = 3
    }

    /// <summary>Loại giải đấu.</summary>
    public enum TournamentType
    {
        [Display(Name = "Giải vô địch quốc gia")] League = 1,
        [Display(Name = "Cúp quốc gia")] Cup = 2,
        [Display(Name = "Giải hạng dưới")] LowerLeague = 3,
        [Display(Name = "Giải trẻ")] Youth = 4,
        [Display(Name = "Giải quốc tế")] International = 5
    }

    /// <summary>Trạng thái cầu thủ.</summary>
    public enum PlayerStatus
    {
        [Display(Name = "Đang thi đấu")] Active = 1,
        [Display(Name = "Chấn thương")] Injured = 2,
        [Display(Name = "Tự do")] FreeAgent = 3,
        [Display(Name = "Giải nghệ")] Retired = 4,
        [Display(Name = "Đã rời giải")] LeftLeague = 5
    }
}
