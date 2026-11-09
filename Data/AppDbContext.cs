using Microsoft.EntityFrameworkCore;
using ScoutVN.Models;

namespace ScoutVN.Data
{
    public class AppDbContext : DbContext
    {
        /// <summary>Collation SQL Server dùng khi tìm kiếm: không phân biệt hoa/thường và không phân biệt dấu ("nguyen" khớp "Nguyễn"; riêng chữ đ cần gõ đúng).</summary>
        public const string SearchCollation = "Latin1_General_100_CI_AI";

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<AppUser> Users => Set<AppUser>();
        public DbSet<Team> Teams => Set<Team>();
        public DbSet<Tournament> Tournaments => Set<Tournament>();
        public DbSet<Player> Players => Set<Player>();
        public DbSet<PlayerStat> PlayerStats => Set<PlayerStat>();
        public DbSet<News> News => Set<News>();
        public DbSet<Match> Matches => Set<Match>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.Username).IsUnique();

            modelBuilder.Entity<Team>()
                .HasIndex(t => t.Name).IsUnique();

            modelBuilder.Entity<Tournament>()
                .HasIndex(t => new { t.Name, t.Season }).IsUnique();

            // Xóa đội bóng => cầu thủ trở thành "tự do" (TeamId = null), không xóa cầu thủ
            modelBuilder.Entity<Player>()
                .HasOne(p => p.Team)
                .WithMany(t => t.Players)
                .HasForeignKey(p => p.TeamId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Player>()
                .HasIndex(p => p.FullName);

            // Xóa cầu thủ / giải đấu => xóa luôn thống kê liên quan
            modelBuilder.Entity<PlayerStat>()
                .HasOne(s => s.Player)
                .WithMany(p => p.Stats)
                .HasForeignKey(s => s.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlayerStat>()
                .HasOne(s => s.Tournament)
                .WithMany(t => t.PlayerStats)
                .HasForeignKey(s => s.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PlayerStat>()
                .HasIndex(s => new { s.PlayerId, s.TournamentId }).IsUnique();

            // Đội thi đấu trong giải: xóa đội thì giữ thống kê, bỏ liên kết
            modelBuilder.Entity<PlayerStat>()
                .HasOne(s => s.Team)
                .WithMany()
                .HasForeignKey(s => s.TeamId)
                .OnDelete(DeleteBehavior.NoAction);

            // Trận đấu: xóa giải => xóa trận; không cho xóa đội khi còn trận đấu (Restrict)
            modelBuilder.Entity<Match>()
                .HasOne(m => m.Tournament)
                .WithMany(t => t.Matches)
                .HasForeignKey(m => m.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Match>()
                .HasOne(m => m.HomeTeam).WithMany()
                .HasForeignKey(m => m.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Match>()
                .HasOne(m => m.AwayTeam).WithMany()
                .HasForeignKey(m => m.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Match>()
                .HasIndex(m => new { m.TournamentId, m.Round });

            modelBuilder.Entity<Player>().HasIndex(p => p.SourceId);

            // Tin tức: xóa giải đấu thì bỏ liên kết, không xóa bài viết
            modelBuilder.Entity<News>()
                .HasOne(n => n.Tournament)
                .WithMany()
                .HasForeignKey(n => n.TournamentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<News>()
                .HasIndex(n => new { n.IsPublished, n.PublishedAt });
        }

        // ===== Làm mới bộ nhớ đệm tra cứu khi dữ liệu thay đổi =====
        // Lưu cầu thủ / đội / giải / trận / thống kê / tài khoản → tăng DataVersion để mọi trang tra cứu
        // thấy ngay dữ liệu mới. Tin tức (kể cả lượt xem) không nằm trong bộ nhớ đệm nên bỏ qua.
        private bool TouchesCachedData() => ChangeTracker.Entries()
            .Any(e => (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted) && e.Entity is not ScoutVN.Models.News);

        public override int SaveChanges()
        {
            var touch = TouchesCachedData();
            var n = base.SaveChanges();
            if (touch) ScoutVN.Services.DataVersion.Bump();
            return n;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var touch = TouchesCachedData();
            var n = await base.SaveChangesAsync(cancellationToken);
            if (touch) ScoutVN.Services.DataVersion.Bump();
            return n;
        }
    }
}
