using Microsoft.EntityFrameworkCore;
using ScoutVN.Models;
using ScoutVN.Services;

namespace ScoutVN.Data
{
    /// <summary>Tạo bảng Tin tức cho CSDL đã có từ các tuần trước và nhập vài bài viết mẫu.</summary>
    public static class NewsSeeder
    {
        /// <summary>
        /// CSDL tạo bằng EnsureCreated ở tuần 4–5 chưa có bảng News; EnsureCreated không tự thêm bảng mới
        /// vào CSDL đã tồn tại nên cần tạo bổ sung bằng SQL (chỉ chạy khi bảng chưa có).
        /// </summary>
        public static async Task EnsureTableAsync(AppDbContext db)
        {
            if (db.Database.GetMigrations().Any()) return; // đã dùng Migration thì để Migration quản lý
            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'[News]', N'U') IS NULL
BEGIN
    CREATE TABLE [News] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Summary] nvarchar(500) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [Category] int NOT NULL,
        [TournamentId] int NULL,
        [IsFeatured] bit NOT NULL,
        [IsPublished] bit NOT NULL,
        [PublishedAt] datetime2 NOT NULL,
        [AuthorName] nvarchar(100) NULL,
        [ViewCount] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_News] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_News_Tournaments_TournamentId] FOREIGN KEY ([TournamentId])
            REFERENCES [Tournaments] ([Id]) ON DELETE SET NULL
    );
    CREATE INDEX [IX_News_IsPublished_PublishedAt] ON [News] ([IsPublished], [PublishedAt]);
    CREATE INDEX [IX_News_TournamentId] ON [News] ([TournamentId]);
END");
        }

        public static async Task SeedAsync(AppDbContext db)
        {
            if (await db.News.AnyAsync()) return;
            var now = DateTime.Now;
            DateTime At(int y, int m, int d, int h = 9) { var t = new DateTime(y, m, d, h, 0, 0); return t <= now ? t : now.AddMinutes(-5); }

            var analysis = new ScoutVN.Services.AnalysisService(db);
            var tours = await db.Tournaments.OrderBy(t => t.Season).ToListAsync();
            var prev = tours.FirstOrDefault(t => t.Season == "2025/26");
            var cur = tours.FirstOrDefault(t => t.Season == "2026/27");
            var list = new List<News>();
            var playerCount = await db.Players.CountAsync();
            var matchCount = await db.Matches.CountAsync();

            list.Add(new News
            {
                Title = "ScoutVN chuyển sang dữ liệu thật của V.League 1",
                Summary = $"Hệ thống đã nạp {playerCount} cầu thủ, {await db.Teams.CountAsync()} câu lạc bộ và {matchCount} trận đấu của hai mùa V.League 1 2025/26 và 2026/27 từ nguồn thống kê công khai FotMob.",
                Content = "ScoutVN là hệ thống Web quản lý, phân tích và hỗ trợ tuyển chọn cầu thủ bóng đá Việt Nam do Nhóm 14 – Trường Đại học Bình Dương xây dựng.\n\n"
                        + "Từ phiên bản này, toàn bộ dữ liệu mẫu sinh ngẫu nhiên đã được thay bằng dữ liệu thật của V.League 1: danh sách cầu thủ (ngày sinh, quốc tịch, vị trí, chiều cao, số áo, giá trị chuyển nhượng), kết quả từng trận, số trận – số phút – đá chính, bàn thắng, phạt đền, thẻ phạt và giữ sạch lưới. Dữ liệu được trích xuất ngày 02/11/2026.\n\n"
                        + "Mùa 2025/26 đã kết thúc có đầy đủ kết quả 182 trận nhưng số liệu cá nhân chỉ gồm bàn thắng và thẻ phạt. Mùa 2026/27 đang diễn ra có số liệu ra sân chi tiết theo từng trận.\n\n"
                        + "Lưu ý: nguồn công khai không cung cấp các chỉ số năng lực (tốc độ, dứt điểm, chuyền bóng, rê bóng, phòng ngự, thể lực) cho V.League. Hệ thống ước lượng 6 chỉ số này từ vị trí, giá trị chuyển nhượng, thời gian thi đấu, số bàn thắng, chiều cao và tuổi, và đánh dấu rõ là “ước tính”. Huấn luyện viên có thể chỉnh lại sau khi đánh giá trực tiếp.",
                Category = NewsCategory.System, IsFeatured = true, PublishedAt = At(2026, 11, 2, 8), AuthorName = "Ban quản trị ScoutVN"
            });

            if (prev != null)
            {
                var st = await analysis.StandingsAsync(prev.Id);
                var top = await db.PlayerStats.Include(s => s.Player).Include(s => s.Team)
                    .Where(s => s.TournamentId == prev.Id).OrderByDescending(s => s.Goals).Take(3).ToListAsync();
                if (st.Count >= 3 && top.Count > 0)
                {
                    var bottom = st.Skip(st.Count - 2).ToList();
                    list.Add(new News
                    {
                        Title = $"Tổng kết V.League 1 2025/26: {st[0].Team.Name} vô địch với {st[0].Points} điểm",
                        Summary = $"{st[0].Team.Name} thắng {st[0].Won}, hòa {st[0].Drawn}, chỉ thua {st[0].Lost} trận sau 26 vòng, hơn {st[1].Team.Name} {st[0].Points - st[1].Points} điểm. {top[0].Player!.FullName} ghi nhiều bàn nhất với {top[0].Goals} bàn.",
                        Content = $"V.League 1 2025/26 khép lại với {st.Sum(r => r.Played) / 2} trận đấu và {st.Sum(r => r.GoalsFor)} bàn thắng (trung bình {(double)st.Sum(r => r.GoalsFor) / (st.Sum(r => r.Played) / 2):0.00} bàn/trận).\n\n"
                                + $"Ngôi vô địch thuộc về {st[0].Team.Name} với {st[0].Points} điểm ({st[0].Won} thắng – {st[0].Drawn} hòa – {st[0].Lost} thua), ghi {st[0].GoalsFor} bàn và chỉ để thủng lưới {st[0].GoalsAgainst} bàn. "
                                + $"Xếp sau là {st[1].Team.Name} ({st[1].Points} điểm) và {st[2].Team.Name} ({st[2].Points} điểm).\n\n"
                                + $"Danh sách ghi bàn: " + string.Join("; ", top.Select((s, i) => $"{i + 1}. {s.Player!.FullName} ({s.Team?.Name ?? "–"}) – {s.Goals} bàn")) + ".\n\n"
                                + $"Hai đội cuối bảng là {bottom[0].Team.Name} ({bottom[0].Points} điểm) và {bottom[1].Team.Name} ({bottom[1].Points} điểm); cả hai không có tên ở V.League 1 mùa 2026/27, thay vào đó là hai tân binh Bắc Ninh FC và Trường Tươi Đồng Nai.\n\n"
                                + "Bảng xếp hạng được ScoutVN tính tự động từ kết quả 182 trận (xếp theo điểm, hiệu số, bàn thắng).",
                        Category = NewsCategory.Tournament, TournamentId = prev.Id, IsFeatured = true,
                        PublishedAt = At(2026, 6, 9), AuthorName = "ScoutVN (tự động từ dữ liệu)"
                    });
                }
            }

            if (cur != null)
            {
                var st = await analysis.StandingsAsync(cur.Id);
                var stats = await db.PlayerStats.Include(s => s.Player).Include(s => s.Team)
                    .Where(s => s.TournamentId == cur.Id).ToListAsync();
                var rounds = await db.Matches.Where(m => m.TournamentId == cur.Id && m.HomeGoals != null).Select(m => m.Round).ToListAsync();
                var lastRound = rounds.Count == 0 ? 0 : rounds.Max();
                var scorers = stats.OrderByDescending(s => s.Goals).ThenBy(s => s.MinutesPlayed).Take(4).ToList();
                var gks = stats.Where(s => s.Player!.Position == Position.GK && s.Appearances > 0).OrderByDescending(s => s.CleanSheets).ThenBy(s => s.GoalsConceded).Take(2).ToList();
                if (st.Count >= 2 && scorers.Count > 0)
                {
                    list.Add(new News
                    {
                        Title = $"V.League 1 2026/27 sau {lastRound} vòng: {st[0].Team.Name} tạm dẫn đầu",
                        Summary = $"{st[0].Team.Name} có {st[0].Points} điểm sau {st[0].Played} trận, {st[1].Team.Name} bám sát với {st[1].Points} điểm. {scorers[0].Player!.FullName} dẫn đầu danh sách ghi bàn ({scorers[0].Goals} bàn).",
                        Content = $"Sau {lastRound} vòng đấu của V.League 1 2026/27, {st[0].Team.Name} đứng đầu với {st[0].Points} điểm ({st[0].Won}T – {st[0].Drawn}H – {st[0].Lost}B, hiệu số {st[0].GoalDiff:+0;-0;0}). "
                                + $"{st[1].Team.Name} ({st[1].Points} điểm) và {st[2].Team.Name} ({st[2].Points} điểm) bám đuổi ngay phía sau, trong khi {st[^1].Team.Name} đang xếp cuối với {st[^1].Points} điểm.\n\n"
                                + "Cuộc đua ghi bàn: " + string.Join("; ", scorers.Select(s => $"{s.Player!.FullName} ({s.Team?.Name ?? "–"}) {s.Goals} bàn sau {s.MinutesPlayed} phút")) + ".\n\n"
                                + (gks.Count > 0 ? "Thủ môn giữ sạch lưới nhiều nhất: " + string.Join("; ", gks.Select(s => $"{s.Player!.FullName} ({s.Team?.Name ?? "–"}) {s.CleanSheets} trận, thủng lưới {s.GoalsConceded} bàn trong {s.Appearances} trận")) + ".\n\n" : "")
                                + "Bảng xếp hạng, lịch thi đấu và thống kê cá nhân được cập nhật tại trang giải đấu.",
                        Category = NewsCategory.Tournament, TournamentId = cur.Id, IsFeatured = true,
                        PublishedAt = At(2026, 11, 2, 7), AuthorName = "ScoutVN (tự động từ dữ liệu)"
                    });
                }

                var young = stats.Where(s => s.Player!.Age <= 22 && s.MinutesPlayed > 0)
                    .OrderByDescending(s => s.MinutesPlayed).Take(5).ToList();
                if (young.Count > 0)
                {
                    list.Add(new News
                    {
                        Title = "Phân tích: 5 cầu thủ từ 22 tuổi trở xuống được trao nhiều phút nhất mùa 2026/27",
                        Summary = "Thời gian thi đấu là chỉ báo quan trọng về độ tin dùng. " + string.Join(", ", young.Select(s => s.Player!.FullName)) + " là những gương mặt trẻ được ra sân nhiều nhất.",
                        Content = "Bài viết được tạo tự động từ số liệu thật V.League 1 2026/27 (nguồn FotMob).\n\n"
                                + string.Join("\n\n", young.Select((s, i) =>
                                    $"{i + 1}. {s.Player!.FullName} – {s.Player.Position.DisplayName()}, {s.Player.Age} tuổi, {s.Team?.Name ?? "–"}: {s.MinutesPlayed} phút sau {s.Appearances} trận ({s.Starts} trận đá chính), {s.Goals} bàn."))
                                + "\n\nHuấn luyện viên có thể dùng chức năng Gợi ý cầu thủ (KNN) với điều kiện “dưới 23 tuổi” để tìm thêm các tài năng trẻ có hồ sơ tương tự.",
                        Category = NewsCategory.Analysis, TournamentId = cur.Id, PublishedAt = At(2026, 11, 1, 20), AuthorName = "ScoutVN (tự động từ dữ liệu)"
                    });
                }

                var news = st.Where(r => r.Team.Name is "Bắc Ninh FC" or "Trường Tươi Đồng Nai").ToList();
                if (news.Count == 2)
                {
                    list.Add(new News
                    {
                        Title = "Hai tân binh Bắc Ninh FC và Trường Tươi Đồng Nai sau chặng đầu V.League 2026/27",
                        Summary = $"{news[0].Team.Name} đứng thứ {news[0].Rank} với {news[0].Points} điểm, {news[1].Team.Name} xếp thứ {news[1].Rank} với {news[1].Points} điểm.",
                        Content = string.Join("\n\n", news.Select(r =>
                                    $"{r.Team.Name}: {r.Played} trận, {r.Won} thắng – {r.Drawn} hòa – {r.Lost} thua, ghi {r.GoalsFor} bàn, thủng lưới {r.GoalsAgainst} bàn, xếp thứ {r.Rank}/{st.Count}."))
                                + "\n\nXem lịch thi đấu và kết quả từng vòng tại trang giải đấu V.League 1 2026/27.",
                        Category = NewsCategory.Tournament, TournamentId = cur.Id, PublishedAt = At(2026, 10, 31, 21), AuthorName = "ScoutVN (tự động từ dữ liệu)"
                    });
                }
            }

            list.Add(new News
            {
                Title = "Hướng dẫn: xếp hạng cầu thủ theo tiêu chí tuyển chọn",
                Summary = "Chọn nhóm vị trí, điều chỉnh trọng số từng chỉ số và để hệ thống tính điểm tổng hợp cho từng ứng viên.",
                Content = "Chức năng Xếp hạng theo tiêu chí cho phép huấn luyện viên tự xác định điều gì quan trọng với vị trí cần tuyển.\n\n"
                        + "Bước 1: chọn nhóm vị trí (thủ môn, hậu vệ, tiền vệ, tiền đạo & cánh). Hệ thống tự nạp bộ tiêu chí mặc định phù hợp.\n\n"
                        + "Bước 2: điều chỉnh trọng số – ví dụ tăng “Bàn thắng / 90 phút” khi cần một tiền đạo săn bàn, hoặc “Tỉ lệ thời gian thi đấu” khi cần cầu thủ được tin dùng.\n\n"
                        + "Bước 3: lọc theo độ tuổi, đội bóng, giải đấu và số phút thi đấu tối thiểu.\n\n"
                        + "Mỗi chỉ số được chuẩn hóa Min–Max về thang 0–1 trong nhóm ứng viên, điểm tổng hợp là trung bình có trọng số quy về thang 100. Kết quả có thể xuất ra Excel (CSV).",
                Category = NewsCategory.Guide, PublishedAt = At(2026, 10, 30), AuthorName = "Ban quản trị ScoutVN"
            });
            list.Add(new News
            {
                Title = "Hướng dẫn: gợi ý cầu thủ bằng thuật toán KNN và trợ lý AI",
                Summary = "Mô tả cầu thủ cần tìm bằng thanh trượt hoặc bằng một câu tiếng Việt, hệ thống trả về các ứng viên gần nhất kèm % phù hợp và lý do.",
                Content = "Trang Gợi ý cầu thủ có hai chế độ: theo tiêu chí (đặt mức mong muốn cho 9 chiều đặc trưng) và tìm cầu thủ tương tự một cầu thủ mẫu.\n\n"
                        + "Hệ thống chuẩn hóa Min–Max các chiều trong nhóm ứng viên, tính khoảng cách Euclid có trọng số và lấy K ứng viên gần nhất. % phù hợp = 100 × (1 − khoảng cách).\n\n"
                        + "Tại trang Trợ lý AI, bạn có thể gõ: “Tìm tiền đạo dưới 23 tuổi ghi bàn tốt” – trợ lý hiểu yêu cầu, gọi KNN và giải thích kết quả.",
                Category = NewsCategory.Guide, PublishedAt = At(2026, 10, 29), AuthorName = "Ban quản trị ScoutVN"
            });

            db.News.AddRange(list);
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Bài viết về cầu thủ & giải đấu, viết từ số liệu thật trong CSDL.
        /// - Gỡ 3 bài của đợt trước (Vua phá lưới, thủ môn, giá trị cầu thủ).
        /// - Chỉ nói về cầu thủ thật của nguồn dữ liệu (có mã FotMob); bỏ qua hồ sơ do người dùng tự thêm,
        ///   và loại trừ riêng "Nguyễn Hữu Tình".
        /// Mỗi bài chỉ thêm một lần (kiểm tra theo đầu tiêu đề) nên chạy được với CSDL đã có tin tức.
        /// </summary>
        public static async Task AddPlayerArticlesAsync(AppDbContext db)
        {
            const string auto = "ScoutVN (viết từ dữ liệu)";
            var oldPrefixes = new[] { "Cuộc đua Vua phá lưới V.League 1", "Những người gác đền đáng tin cậy nhất", "Những cầu thủ có giá trị cao nhất V.League 1" };
            var old = (await db.News.Where(n => n.AuthorName == auto).ToListAsync())
                .Where(n => oldPrefixes.Any(pre => n.Title.StartsWith(pre))).ToList();
            if (old.Count > 0) { db.News.RemoveRange(old); await db.SaveChangesAsync(); }

            var cur = await db.Tournaments.Where(t => t.HasMatchDetails).OrderByDescending(t => t.Season).FirstOrDefaultAsync();
            if (cur == null) return;
            var titles = await db.News.Select(n => n.Title).ToListAsync();
            bool Has(string prefix) => titles.Any(t => t.StartsWith(prefix));
            var vi = Fmt.VI;
            var now = DateTime.Now;
            DateTime At(int y, int m, int d, int h) { var t = new DateTime(y, m, d, h, 0, 0); return t <= now ? t : now.AddMinutes(-5); }
            static bool Excluded(Player p) => p.SourceId == null || TextHelper.RemoveDiacritics(p.FullName).ToLowerInvariant().Contains("nguyen huu tinh");

            var analysis = new AnalysisService(db);
            var rows = (await analysis.LoadRowsAsync(cur.Id))
                .Where(r => r.Stat != null && !Excluded(r.Player) && r.Player.TeamId != null).ToList();
            var rounds = await db.Matches.Where(m => m.TournamentId == cur.Id && m.HomeGoals != null).Select(m => m.Round).ToListAsync();
            var lastRound = rounds.Count == 0 ? 0 : rounds.Max();
            var teamGames = lastRound;
            string T(PlayerRow r) => r.Player.Team?.Name ?? "–";
            var list = new List<News>();

            // 1) Đội hình tiêu biểu (4-3-3) theo tiêu chí mặc định của từng tuyến
            const string t1 = "Đội hình tiêu biểu V.League 1";
            if (!Has(t1))
            {
                List<PlayerRow> Pick(PositionGroup g, int n)
                {
                    var pool = rows.Where(r => PositionGroups.Of(r.Player.Position) == g && r.Stat!.Minutes >= 270).ToList();
                    var w = CoachNeeds.MetricWeights(CoachNeeds.Defaults(g)).ToDictionary(kv => kv.Key, kv => kv.Value * 10);
                    return analysis.Rank(pool, w).Take(n).Select(x => x.Row).ToList();
                }
                var gk = Pick(PositionGroup.GK, 1); var df = Pick(PositionGroup.DEF, 4); var md = Pick(PositionGroup.MID, 3); var at = Pick(PositionGroup.ATT, 3);
                if (gk.Count == 1 && df.Count == 4 && md.Count == 3 && at.Count == 3)
                {
                    string Line(PlayerRow r) => $"{r.Player.FullName} ({r.Player.Position}, {T(r)}) – {r.Stat!.Minutes} phút" + (r.Stat.Goals > 0 ? $", {r.Stat.Goals} bàn" : "");
                    var all = gk.Concat(df).Concat(md).Concat(at).ToList();
                    var groups = all.GroupBy(T).ToList();
                    var maxN = groups.Max(g => g.Count());
                    var tops = groups.Where(g => g.Count() == maxN).Select(g => g.Key).ToList();
                    var topText = tops.Count == 1 ? $"{tops[0]} góp {maxN} cái tên" : $"{string.Join(" và ", tops)} cùng góp {maxN} cái tên";
                    list.Add(new News
                    {
                        Title = $"{t1} {cur.Season} sau {lastRound} vòng: {topText}",
                        Summary = $"Đội hình 4-3-3 được ScoutVN chọn bằng điểm phù hợp theo từng tuyến: {gk[0].Player.FullName} bắt chính, hàng công gồm {string.Join(", ", at.Select(r => r.Player.FullName))}.",
                        Content = $"Đội hình tiêu biểu được chọn tự động từ số liệu thật sau {lastRound} vòng V.League 1 {cur.Season}: mỗi tuyến xếp hạng theo bộ tiêu chí mặc định của trang Xếp hạng (thủ môn: phản xạ, giữ sạch lưới, bền bỉ; hậu vệ: phòng ngự, tì đè, không chiến; tiền vệ: chuyền bóng, rê dắt, bền bỉ; tiền đạo: dứt điểm, tốc độ), chỉ xét cầu thủ đá từ 270 phút.\n\n"
                                + $"Thủ môn: {Line(gk[0])}, {gk[0].Stat!.CleanSheets} trận sạch lưới.\n\n"
                                + "Hậu vệ:\n" + string.Join("\n", df.Select(Line)) + "\n\n"
                                + "Tiền vệ:\n" + string.Join("\n", md.Select(Line)) + "\n\n"
                                + "Tiền đạo:\n" + string.Join("\n", at.Select(Line)) + "\n\n"
                                + (tops.Count == 1 ? $"{tops[0]} là đội có nhiều đại diện nhất ({maxN} cầu thủ). " : $"{string.Join(" và ", tops)} là những đội có nhiều đại diện nhất ({maxN} cầu thủ mỗi đội). ")
                                + $"Đội hình được cập nhật lại khi có số liệu các vòng tiếp theo; HLV có thể tự chọn đội hình theo tiêu chí riêng tại trang Xếp hạng theo tiêu chí.",
                        Category = NewsCategory.Analysis, TournamentId = cur.Id, IsFeatured = false,
                        PublishedAt = At(2026, 11, 5, 9), AuthorName = auto
                    });
                }
            }

            // 2) Ngoại binh nổi bật
            const string t2 = "Ngoại binh nổi bật V.League 1";
            var foreign = rows.Where(r => r.Player.Nationality != "Việt Nam" && r.Stat!.Minutes >= 270).ToList();
            if (!Has(t2) && foreign.Count >= 5)
            {
                var att = foreign.Where(r => r.Stat!.Goals > 0).OrderByDescending(r => r.Stat!.Goals).ThenBy(r => r.Stat!.Minutes).Take(3).ToList();
                var iron = foreign.Where(r => PositionGroups.Of(r.Player.Position) == PositionGroup.DEF).OrderByDescending(r => r.Stat!.Minutes).Take(2).ToList();
                var nations = foreign.GroupBy(r => r.Player.Nationality).OrderByDescending(g => g.Count()).Take(3).ToList();
                if (att.Count >= 2)
                    list.Add(new News
                    {
                        Title = $"{t2} {cur.Season}: {att[0].Player.FullName} và những cái tên tạo khác biệt",
                        Summary = $"{foreign.Count} ngoại binh đã đá từ 270 phút trở lên. {att[0].Player.FullName} ({T(att[0])}) đang là chân sút ngoại hiệu quả nhất với {att[0].Stat!.Goals} bàn.",
                        Content = $"Sau {lastRound} vòng V.League 1 {cur.Season}, có {foreign.Count} ngoại binh được ra sân từ 270 phút trở lên, đến nhiều nhất từ "
                                + string.Join(", ", nations.Select(g => $"{g.Key} ({g.Count()} cầu thủ)")) + ".\n\n"
                                + "Trên hàng công, những ngoại binh ghi bàn nhiều nhất:\n"
                                + string.Join("\n", att.Select((r, i) => $"{i + 1}. {r.Player.FullName} ({r.Player.Nationality}, {T(r)}) – {r.Stat!.Goals} bàn sau {r.Stat.Minutes} phút, {(r.Stat.Goals * 90.0 / r.Stat.Minutes).ToString("0.00", vi)} bàn/90 phút"))
                                + "\n\n"
                                + (iron.Count > 0 ? "Ở hàng thủ, " + string.Join(" và ", iron.Select(r => $"{r.Player.FullName} ({T(r)}, {r.Stat!.Minutes} phút)")) + " là những trụ cột được tin dùng nhiều nhất.\n\n" : "")
                                + "Với quy định số ngoại binh được đăng ký có hạn, chất lượng của nhóm cầu thủ này thường quyết định vị trí của đội trên bảng xếp hạng. Có thể lọc “Ngoại binh” ở trang Gợi ý KNN hoặc Xếp hạng theo tiêu chí để so sánh chi tiết.",
                        Category = NewsCategory.Tournament, TournamentId = cur.Id,
                        PublishedAt = At(2026, 11, 6, 9), AuthorName = auto
                    });
            }

            // 3) Những cầu thủ không thể thay thế (đá trọn mọi phút)
            const string t3 = "Những cầu thủ không thể thay thế";
            var full = rows.Where(r => teamGames > 0 && r.Stat!.Minutes >= teamGames * 90).OrderBy(r => r.Player.Position).ThenBy(r => r.Player.FullName).ToList();
            if (!Has(t3) && full.Count >= 3)
            {
                var vn = full.Count(r => r.Player.Nationality == "Việt Nam");
                var fg = full.GroupBy(T).ToList(); var fMax = fg.Max(g => g.Count());
                var fTops = fg.Where(g => g.Count() == fMax).Select(g => g.Key).ToList();
                var youngest = full.OrderBy(r => r.Player.Age).First();
                list.Add(new News
                {
                    Title = $"{t3}: {full.Count} cầu thủ đá trọn {teamGames * 90} phút sau {lastRound} vòng",
                    Summary = $"{full.Count} cầu thủ chưa vắng mặt phút nào ở V.League 1 {cur.Season}, trong đó {vn} cầu thủ Việt Nam. Trẻ nhất là {youngest.Player.FullName} ({youngest.Player.Age} tuổi).",
                    Content = $"Thời gian thi đấu là thước đo rõ nhất cho độ tin dùng của huấn luyện viên. Sau {lastRound} vòng V.League 1 {cur.Season}, có {full.Count} cầu thủ đá trọn vẹn {teamGames * 90} phút:\n\n"
                            + string.Join("\n", full.Select(r => $"• {r.Player.FullName} – {r.Player.Position.DisplayName()}, {r.Player.Age} tuổi, {T(r)}" + (r.Stat!.Goals > 0 ? $", {r.Stat.Goals} bàn" : "")))
                            + "\n\n"
                            + $"{string.Join(" và ", fTops)} có nhiều cầu thủ thuộc nhóm này nhất ({fMax} người{(fTops.Count > 1 ? " mỗi đội" : "")}), cho thấy sự ổn định trong đội hình xuất phát. "
                            + $"Đáng chú ý, {youngest.Player.FullName} mới {youngest.Player.Age} tuổi nhưng đã là trụ cột không thể thay thế của {T(youngest)}.\n\n"
                            + "Tiêu chí “Bền bỉ, ra sân thường xuyên” trên trang Gợi ý KNN giúp tìm nhanh những cầu thủ có mức độ được sử dụng cao như nhóm này.",
                    Category = NewsCategory.Analysis, TournamentId = cur.Id,
                    PublishedAt = At(2026, 11, 7, 9), AuthorName = auto
                });
            }

            if (list.Count == 0) return;
            db.News.AddRange(list);
            await db.SaveChangesAsync();
        }
    }
}
