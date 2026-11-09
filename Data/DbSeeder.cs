using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScoutVN.Models;

namespace ScoutVN.Data
{
    /// <summary>
    /// Khởi tạo CSDL và nhập DỮ LIỆU THẬT của V.League 1 (mùa 2025/26 và 2026/27).
    /// - Nguồn: trang thống kê công khai FotMob (giải 9088), trích xuất ngày 02/11/2026.
    /// - Tệp Data/Seed/vleague_seed.json được tạo bởi tools/build_seed.py (chuẩn hóa tên, vị trí, giá trị,
    ///   ước lượng 6 chỉ số năng lực) và được nhúng vào assembly.
    /// </summary>
    public static class DbSeeder
    {
        public const string SeedResource = "ScoutVN.Seed.vleague.json";

        /// <summary>
        /// CSDL cũ (tạo bằng EnsureCreated) chưa có cột Users.IsApproved → thêm cột, mặc định 1
        /// để các tài khoản đang có vẫn đăng nhập bình thường. Chỉ chạy khi cột chưa có.
        /// </summary>
        private static async Task EnsureUserApprovalColumnAsync(AppDbContext db)
        {
            if (db.Database.GetMigrations().Any()) return;
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'Users', N'IsApproved') IS NULL
    ALTER TABLE [Users] ADD [IsApproved] bit NOT NULL CONSTRAINT [DF_Users_IsApproved] DEFAULT (1);");
        }

        public static async Task InitializeAsync(AppDbContext db, IPasswordHasher<AppUser> hasher)
        {
            // Có migration thì dùng migration, chưa có thì tạo CSDL trực tiếp từ model
            if (db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync();
            else
                await db.Database.EnsureCreatedAsync();

            await EnsureUserApprovalColumnAsync(db);
            if (!await db.Users.AnyAsync())
            {
                var admin = new AppUser { Username = "admin", FullName = "Quản trị hệ thống", Email = "admin@scoutvn.local", Role = UserRole.Admin };
                admin.PasswordHash = hasher.HashPassword(admin, "Admin@123");
                var scout = new AppUser { Username = "hlv", FullName = "HLV Demo", Email = "hlv@scoutvn.local", Role = UserRole.Scout };
                scout.PasswordHash = hasher.HashPassword(scout, "Hlv@123");
                db.Users.AddRange(admin, scout);
                await db.SaveChangesAsync();
            }

            if (!await db.Teams.AnyAsync())
                await SeedVLeagueAsync(db);
            await FillMissingFootAsync(db);

            await NewsSeeder.EnsureTableAsync(db);
            await NewsSeeder.SeedAsync(db);
            await NewsSeeder.AddPlayerArticlesAsync(db);
        }

        // ===== Cấu trúc tệp seed =====
        private record SeedFile(string Source, string RefDate, List<SeedTeam> Teams, List<SeedPlayer> Players, List<SeedTournament> Tournaments);
        private record SeedTeam(int Src, string Name, string Short, string City, string? Stadium, string? Coach, string Desc);
        private record SeedPlayer(int Src, string Name, string Dob, string Nat, string Pos, string Foot, int? Height, int? Shirt,
            int? Team, string Status, int? Value, int[] Attr, string? Notes);
        private record SeedTournament(string Key, string Name, string Season, string Start, string? End, bool Detail, string Desc,
            List<SeedStat> Stats, List<SeedMatch> Matches);
        private record SeedStat(int P, int T, int Apps, int Starts, int Min, int G, int? Pen, int Yc, int Rc, int Cs, int Gc);
        private record SeedMatch(int Src, int Round, string Utc, int H, int A, int Hg, int Ag);

        public static string ReadSeedJson()
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(SeedResource)
                ?? throw new InvalidOperationException("Không tìm thấy dữ liệu nhúng " + SeedResource);
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        /// <summary>
        /// Bổ sung chân thuận cho CSDL đã tạo từ trước: chỉ điền những cầu thủ đang "Chưa rõ" (không ghi đè dữ liệu Admin đã sửa).
        /// Lưu ý: phần lớn giá trị chân thuận trong tệp seed là GIẢ LẬP (nguồn FotMob chỉ có ~4% cầu thủ), xem tools/build_seed.py.
        /// </summary>
        private static async Task FillMissingFootAsync(AppDbContext db)
        {
            var unknown = await db.Players.Where(p => p.PreferredFoot == PreferredFoot.Unknown && p.SourceId != null).ToListAsync();
            if (unknown.Count == 0) return;
            var seed = JsonSerializer.Deserialize<SeedFile>(ReadSeedJson(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var feet = seed.Players.GroupBy(p => p.Src).ToDictionary(g => g.Key, g => g.First().Foot);
            var changed = 0;
            foreach (var p in unknown)
                if (feet.TryGetValue(p.SourceId!.Value, out var f) && Enum.TryParse<PreferredFoot>(f, out var pf) && pf != PreferredFoot.Unknown)
                { p.PreferredFoot = pf; changed++; }
            if (changed > 0) await db.SaveChangesAsync();
        }

        private static async Task SeedVLeagueAsync(AppDbContext db)
        {
            var seed = JsonSerializer.Deserialize<SeedFile>(ReadSeedJson(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // ===== Đội bóng =====
            var teams = seed.Teams.Select(t => new Team
            {
                Name = t.Name, ShortName = t.Short, City = t.City, Stadium = t.Stadium,
                HeadCoach = t.Coach, Description = t.Desc, SourceId = t.Src
            }).ToList();
            db.Teams.AddRange(teams);
            await db.SaveChangesAsync();
            var teamBySrc = teams.ToDictionary(t => t.SourceId!.Value, t => t.Id);

            // ===== Cầu thủ =====
            var players = seed.Players.Select(p => new Player
            {
                SourceId = p.Src,
                FullName = p.Name,
                DateOfBirth = DateTime.Parse(p.Dob),
                Nationality = p.Nat,
                Position = Enum.Parse<Position>(p.Pos),
                PreferredFoot = Enum.Parse<PreferredFoot>(p.Foot),
                HeightCm = p.Height is >= 140 and <= 220 ? p.Height : null,
                ShirtNumber = p.Shirt,
                TeamId = p.Team.HasValue && teamBySrc.TryGetValue(p.Team.Value, out var tid) ? tid : null,
                Status = Enum.Parse<PlayerStatus>(p.Status),
                MarketValue = p.Value,
                Pace = p.Attr[0], Shooting = p.Attr[1], Passing = p.Attr[2],
                Dribbling = p.Attr[3], Defending = p.Attr[4], Physical = p.Attr[5],
                AttributesEstimated = true,
                Notes = p.Notes,
                UpdatedAt = DateTime.Parse(seed.RefDate)
            }).ToList();
            db.Players.AddRange(players);
            await db.SaveChangesAsync();
            var playerBySrc = players.ToDictionary(p => p.SourceId!.Value, p => p.Id);

            // ===== Giải đấu, thống kê, trận đấu =====
            foreach (var st in seed.Tournaments)
            {
                var tour = new Tournament
                {
                    Name = st.Name, Season = st.Season, Type = TournamentType.League,
                    Organizer = "Công ty CP Bóng đá chuyên nghiệp Việt Nam (VPF)",
                    StartDate = DateTime.Parse(st.Start), EndDate = st.End == null ? null : DateTime.Parse(st.End),
                    HasMatchDetails = st.Detail, Description = st.Desc
                };
                db.Tournaments.Add(tour);
                await db.SaveChangesAsync();

                db.PlayerStats.AddRange(st.Stats.Where(s => playerBySrc.ContainsKey(s.P)).Select(s => new PlayerStat
                {
                    PlayerId = playerBySrc[s.P], TournamentId = tour.Id,
                    TeamId = teamBySrc.TryGetValue(s.T, out var t) ? t : null,
                    Appearances = s.Apps, Starts = s.Starts, MinutesPlayed = s.Min,
                    Goals = s.G, PenaltyGoals = s.Pen, YellowCards = s.Yc, RedCards = s.Rc,
                    CleanSheets = s.Cs, GoalsConceded = s.Gc
                }));
                db.Matches.AddRange(st.Matches.Select(m => new Match
                {
                    TournamentId = tour.Id, Round = m.Round, SourceId = m.Src,
                    // Giờ thi đấu lưu theo giờ Việt Nam (UTC+7)
                    KickOff = DateTime.Parse(m.Utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal).AddHours(7),
                    HomeTeamId = teamBySrc[m.H], AwayTeamId = teamBySrc[m.A],
                    HomeGoals = m.Hg, AwayGoals = m.Ag
                }));
                await db.SaveChangesAsync();
            }
        }
    }
}
