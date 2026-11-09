# ScoutVN – Hệ thống quản lý, phân tích và hỗ trợ tuyển chọn cầu thủ bóng đá Việt Nam

Đồ án ngành – Nhóm 14 · GVHD: ThS. Nguyễn Hồ Hải
Nguyễn Hữu Tình (23050076) · Ngô Đức Phôn (23050080) · Phạm Quốc Nguyên (23050069)

Công nghệ: **ASP.NET Core 8 MVC**, **Entity Framework Core 8**, **SQL Server**, Bootstrap 5, xác thực bằng Cookie.

---

## 1. Cài đặt và chạy

**Cần có:**

- Visual Studio 2022 (workload *ASP.NET and web development*), hoặc .NET 8 SDK + VS Code
- SQL Server LocalDB (cài kèm Visual Studio) hoặc SQL Server Express/Developer

**Các bước:**

1. Mở `ScoutVN.csproj` (hoặc thư mục dự án) bằng Visual Studio.
2. Khai báo chuỗi kết nối CSDL. **Không ghi vào `appsettings.json` / mã nguồn** (mục này để trống); chọn một trong các cách:
   - Tạo tệp `appsettings.Local.json` cạnh `appsettings.json` (tệp này đã nằm trong `.gitignore`, không bị đưa lên git):
     ```json
     { "ConnectionStrings": { "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ScoutVN_VLeague;Trusted_Connection=True;TrustServerCertificate=True" } }
     ```
     (SQL Server Express: `Server=.\\SQLEXPRESS;...`; tài khoản SQL: `Server=TÊN_MÁY;Database=ScoutVN_VLeague;User Id=...;Password=...;TrustServerCertificate=True`)
   - Hoặc dùng user-secrets: `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<chuỗi kết nối>"`
   - Khi triển khai lên máy chủ: biến môi trường `ConnectionStrings__DefaultConnection`.
3. Nhấn **F5** (hoặc chạy `dotnet run`). Lần chạy đầu tiên hệ thống sẽ **tự tạo CSDL `ScoutVN_VLeague` và nhập dữ liệu thật V.League 1** (mục 2f).

**Tài khoản demo:**

| Vai trò | Tên đăng nhập | Mật khẩu |
|---|---|---|
| Quản trị viên | `admin` | `Admin@123` |
| HLV / Tuyển trạch viên | `hlv` | `Hlv@123` |

**Nạp lại dữ liệu từ đầu:** xóa database `ScoutVN_VLeague` (SSMS hoặc SQL Server Object Explorer) rồi chạy lại.

> Khi sang các tuần sau có thêm bảng mới, nhóm nên chuyển sang **Migration**:
> `Add-Migration InitialCreate` → `Update-Database` (Package Manager Console). Khi đã có migration,
> chương trình tự gọi `Migrate()` thay cho `EnsureCreated()` (xem `Data/DbSeeder.cs`).

---

## 2. Nội dung Tuần 4 (29/09 – 05/10/2026)

| Yêu cầu trong đề cương | Đã thực hiện |
|---|---|
| Xây dựng chức năng quản lý cầu thủ, đội bóng và giải đấu | `PlayersController`, `TeamsController`, `TournamentsController`, kèm `PlayerStatsController` (dữ liệu thống kê thi đấu) |
| Xây dựng chức năng thêm, sửa, xóa và tìm kiếm dữ liệu | Đủ CRUD cho cả 4 đối tượng; tìm kiếm không phân biệt hoa/thường và dấu; lọc nhiều tiêu chí; sắp xếp; phân trang |
| Nhập dữ liệu mẫu | `Data/DbSeeder.cs`: 14 CLB V.League 1 2025/26, 4 giải đấu, khoảng 236 cầu thủ, hơn 600 bản ghi thống kê |

Phần nền tảng của tuần 3 cũng có sẵn: đăng nhập, đăng ký, đăng xuất, phân quyền Admin/HLV, giao diện quản trị và quản lý người dùng.

### Chức năng chi tiết

- **Cầu thủ:** lọc theo tên/quê quán, đội (hoặc cầu thủ tự do), vị trí, khoảng tuổi, quốc tịch (hoặc toàn bộ ngoại binh), chân thuận, trạng thái, chiều cao tối thiểu. Sắp xếp theo OVR, tên, tuổi, giá trị, đội, vị trí. Hồ sơ gồm 6 chỉ số năng lực, bảng thống kê theo từng giải và tổng sự nghiệp.
- **Đội bóng:** tìm theo tên, viết tắt hoặc sân; lọc theo tỉnh/thành. Danh sách hiển thị số cầu thủ, số ngoại binh, OVR trung bình. Trang chi tiết có đội hình.
- **Giải đấu:** lọc theo mùa và loại giải. Trang chi tiết có bảng xếp hạng vua phá lưới, kiến tạo và điểm trung bình.
- **Thống kê thi đấu:** nhập, sửa, xóa số liệu từng cầu thủ theo từng giải; lọc theo giải và đội; sắp xếp theo bàn, kiến tạo, điểm, số trận, tắc/cắt bóng, cứu thua.
- **Bảng điều khiển:** số liệu tổng quan, top OVR, vua phá lưới mùa hiện tại, phân bố cầu thủ theo vị trí.

### Ràng buộc dữ liệu (kiểm tra ở server, thông báo tiếng Việt)

- Tên đội là duy nhất. Mỗi cặp (tên giải, mùa giải) là duy nhất. Mỗi cặp (cầu thủ, giải đấu) chỉ có một bản ghi thống kê.
- Số áo không được trùng trong cùng một đội. Tuổi cầu thủ phải từ 14 đến 45. Ngày kết thúc giải phải sau ngày bắt đầu.
- Thống kê phải hợp lý: số phút ≤ số trận × 120; số cú sút trúng đích ≥ số bàn thắng.
- Xóa đội bóng thì cầu thủ của đội chuyển sang **Tự do** (không bị xóa). Xóa cầu thủ hoặc giải đấu thì thống kê liên quan bị xóa theo (có cảnh báo trước khi xóa).
- HLV/Tuyển trạch viên chỉ được xem. Thêm, sửa, xóa chỉ dành cho Admin.

---

## 2b. Nội dung Tuần 5 (06/10 – 12/10/2026)

| Yêu cầu trong đề cương | Đã thực hiện |
|---|---|
| Xây dựng chức năng thống kê và phân tích hiệu suất cầu thủ | `/Analysis` thống kê tổng quan theo giải; `/Analysis/Player/{id}` phân tích từng cầu thủ: radar so với trung bình cùng nhóm vị trí, **phân vị** từng chỉ số, tự rút ra điểm mạnh / điểm cần cải thiện, diễn biến qua các mùa |
| Xây dựng chức năng lọc, xếp hạng và so sánh cầu thủ | `/Analysis/Ranking` xếp hạng theo **bộ tiêu chí + trọng số** (mặc định theo nhóm vị trí, chỉnh bằng thanh trượt), lọc theo vị trí/tuổi/đội/giải/số phút tối thiểu; `/Analysis/Compare` so sánh 2–4 cầu thủ (chọn từ danh sách hoặc tick ở trang Cầu thủ) |
| Trực quan hóa dữ liệu bằng biểu đồ | Chart.js (đóng gói sẵn trong `wwwroot/lib/chartjs`): cột, cột nhóm, radar, đường, phân bố OVR/độ tuổi; click vào cột để mở phân tích cầu thủ |
| (Bổ sung) Xuất báo cáo | Nút “Xuất Excel (CSV)” ở trang So sánh và Xếp hạng (UTF-8 có BOM, mở bằng Excel không lỗi font) |

**Cách tính:**

- Số liệu cộng dồn theo phạm vi đã chọn (một giải hoặc “Tất cả giải”). % chuyền chính xác và điểm TB được bình quân theo số phút. Các chỉ số “/90'” = giá trị × 90 / số phút.
- Xếp hạng: chuẩn hóa Min–Max từng tiêu chí trong nhóm ứng viên về [0, 1]. Tiêu chí “càng ít càng tốt” (thẻ phạt) được đảo chiều. Điểm = Σ(wᵢ·xᵢ)/Σwᵢ × 100. Khi có tiêu chí thống kê, chỉ xét cầu thủ đủ số phút tối thiểu (mặc định 450') để tránh mẫu quá nhỏ.
- Phân vị = % cầu thủ cùng nhóm vị trí có giá trị không tốt hơn cầu thủ đang xét. Chỉ số thi đấu so với nhóm có ≥ 270 phút.
- Mã nguồn: `Services/AnalysisService.cs` (danh mục chỉ số `Metrics`, bộ trọng số mặc định `Metrics.Preset`, `Rank`, `Percentiles`). Phần chuẩn hóa này sẽ được dùng lại cho KNN ở tuần 6.

---

## 2c. Cổng thông tin công khai (không cần đăng nhập)

Trang chủ `/` hiện là cổng thông tin công khai. Bảng điều khiển nội bộ chuyển sang `/Home/Dashboard` (sau khi đăng nhập hệ thống tự chuyển tới).

| Đường dẫn | Nội dung |
|---|---|
| `/` | Hero + vua phá lưới hiện tại, số liệu tổng quan, **tin nổi bật** (bài được ghim), **điểm nhấn số liệu** tự sinh từ dữ liệu (vua phá lưới, kiến tạo, phong độ, thủ môn, tài năng trẻ, hàng công mạnh nhất), bảng xếp hạng cá nhân (ghi bàn / kiến tạo / điểm TB / thủ môn), giải đấu, đội bóng |
| `/giai-dau`, `/giai-dau/{id}` | Danh sách giải theo mùa; chi tiết giải: số liệu, bảng xếp hạng top 20, biểu đồ bàn thắng theo đội, tin liên quan |
| `/tin-tuc`, `/tin-tuc/{id}/{slug}` | Danh sách tin (lọc chuyên mục, tìm kiếm, phân trang, đọc nhiều nhất); chi tiết bài viết (đếm lượt xem, bài khác) |
| `/doi-bong`, `/doi-bong/{id}` | Danh sách đội; đội hình kèm số liệu mùa hiện tại |
| `/cau-thu/{id}` | Hồ sơ cơ bản: thông tin, 6 chỉ số năng lực, thống kê theo giải |

Phân tích chuyên sâu (phân vị, so sánh, xếp hạng theo tiêu chí, gợi ý AI) **vẫn yêu cầu đăng nhập**. Trang công khai có nút dẫn tới các công cụ này.

**Quản lý tin tức (Admin):** menu *Hệ thống → Tin tức* (`/News`).

- Viết, sửa, xóa bài; chọn chuyên mục và giải đấu liên quan.
- Ghim nổi bật hoặc bỏ ghim; xuất bản hoặc chuyển về nháp. Bản nháp chỉ Admin xem trước được.
- Nội dung là văn bản thuần, các đoạn cách nhau một dòng trống, nên không lo chèn mã độc HTML.

**Cơ sở dữ liệu:** bảng mới `News`.

- CSDL tạo từ tuần 4–5 sẽ được tự động bổ sung bảng này khi chạy lần đầu (`Data/NewsSeeder.cs`), kèm 5 bài viết mẫu.
- Bài gắn nhãn “[Dữ liệu mẫu]” được sinh từ số liệu giả lập.

---

## 2d. Tuần 6–7: Gợi ý cầu thủ (KNN) & Trợ lý AI

| Chức năng | Đường dẫn | Mô tả |
|---|---|---|
| Gợi ý theo tiêu chí | `/Recommend` | Chọn nhóm vị trí, tuổi, số phút, K; nhập mức mong muốn & mức độ quan trọng (0–3) của 9 chỉ số |
| Tìm cầu thủ tương tự | `/Recommend?mode=similar&playerId=..` | Vector của cầu thủ mẫu làm mục tiêu (nút "Tìm cầu thủ tương tự" ở trang phân tích) |
| Trợ lý AI | `/Assistant` | Hỏi tiếng Việt, VD: "Tìm 5 tiền đạo dưới 23 tuổi ghi bàn tốt và có tốc độ" |

**KNN** (`Services/KnnService.cs`): vector 9 chiều (6 chỉ số năng lực + bàn/90', kiến tạo/90', điểm TB) → Min–Max theo nhóm ứng viên → `d = √(Σ wⱼ(xⱼ − tⱼ)² / Σ wⱼ)` → K nhỏ nhất; % phù hợp = 100·(1 − d).

**Trợ lý AI** (`Services/AiAssistantService.cs`): có khóa API → LLM tự gọi công cụ tra cứu CSDL (mục 2h); chưa có khóa hoặc dịch vụ lỗi → tra cứu theo mẫu câu (StatsQaService) và gợi ý KNN.

### Cấu hình khóa API (chỉ đặt ở máy chủ, KHÔNG commit lên Git)

```bash
# Google Gemini (mặc định)
dotnet user-secrets init
dotnet user-secrets set "AiAssistant:ApiKey" "<khóa Gemini>"
# Hoặc OpenAI / dịch vụ tương thích
dotnet user-secrets set "AiAssistant:Provider" "openai"
dotnet user-secrets set "AiAssistant:Model" "gpt-4o-mini"
dotnet user-secrets set "AiAssistant:ApiKey" "<khóa OpenAI>"
# Hoặc Claude (Anthropic) – khóa lấy tại console.anthropic.com
dotnet user-secrets set "AiAssistant:Provider" "claude"
dotnet user-secrets set "AiAssistant:Model" "claude-sonnet-5-5"   # rẻ, nhanh hơn: claude-haiku-5-5
dotnet user-secrets set "AiAssistant:ApiKey" "<khóa Claude, dạng sk-ant-...>"
```

Có thể dùng biến môi trường `AiAssistant__ApiKey`, hoặc điền mục `AiAssistant` trong `appsettings.json` (chỉ khi chạy thử trên máy cá nhân).

## 2e. Tuần 8: Chuyển trang nội dung bằng nút số

- Trang dài (Thống kê & phân tích, Phân tích cầu thủ, So sánh, Xếp hạng, Gợi ý KNN, Bảng điều khiển) được chia thành nhiều phần: bấm nút số ở đầu trang hoặc thanh « 1 2 3 » ở cuối trang để chuyển phần, không phải cuộn. Phần đang xem lưu trên URL (`#trang-2`) nên tải lại trang vẫn giữ nguyên.
- Sau khi nhấn **Xếp hạng** / **Gợi ý**, trang tự mở phần Kết quả.
- Bảng kết quả dài (xếp hạng, gợi ý KNN, đội hình) chia trang 10 dòng ngay trên trình duyệt.
- Cách dùng cho trang mới: bọc nội dung trong `<div class="sv-paged">` gồm các `<section class="sv-page" data-title="...">`; bảng thêm `data-page-size="10"` (mã ở `wwwroot/js/site.js`).

## 2f. Tuần 9: Dữ liệu thật V.League 1 & hoàn thiện

**Nguồn dữ liệu:** trang thống kê công khai FotMob (giải 9088 – V.League 1), trích xuất ngày 02/11/2026 → `tools/fotmob_export_2026-11-02.json`.
Script `tools/build_seed.py` làm sạch và tạo `Data/Seed/vleague_seed.json` (nhúng vào assembly, `DbSeeder` đọc khi tạo CSDL):

| Nội dung | Số lượng |
|---|---|
| Câu lạc bộ (14 đội 2026/27 + 2 đội xuống hạng sau 2025/26) | 16 |
| Cầu thủ (loại 81 hồ sơ thiếu ngày sinh hoặc không có vị trí lẫn số liệu) | 498 |
| Trận đấu: 2025/26 (26 vòng, đủ 182 trận) + 2026/27 (6 vòng, 42 trận) | 224 |
| Bản ghi thống kê: 2025/26 (bàn thắng, phạt đền, thẻ) + 2026/27 (trận, đá chính, phút, bàn, thẻ, sạch lưới, bàn thua) | 296 + 317 |

Xử lý chính: đưa họ lên trước với tên Việt (FotMob ghi “Tên Đệm Họ”); ánh xạ vị trí (LM→LW, RM→RW, LWB→LB, RWB→RB, CDM→DM, CAM→AM); giá trị chuyển nhượng EUR → triệu VNĐ (× 0,03).

**Chỉ số năng lực ƯỚC TÍNH:** FotMob không công bố 6 chỉ số (tốc độ, dứt điểm, chuyền, rê, phòng ngự, thể lực) cho V.League nên hệ thống ước lượng:
`chỉ số = mẫu theo vị trí + (q − 0,5)·20 + điều chỉnh (bàn thắng → dứt điểm, chiều cao → thể lực, tuổi → tốc độ, sạch lưới → phòng ngự TM) + nhiễu ±3`, kẹp 35–90;
`q` = trung bình có trọng số của phân vị giá trị chuyển nhượng (0,40), tỉ lệ thời gian thi đấu 2026/27 (0,35), phân vị bàn thắng trong nhóm vị trí hoặc tỉ lệ đá chính (0,25).
Các chỉ số này được gắn nhãn “ước tính” trên giao diện; Admin sửa lại được sau khi HLV đánh giá trực tiếp.

**Thay đổi mô hình:** thêm bảng `Matches` (bảng xếp hạng đội tính tự động: 3/1/0 điểm, xếp theo điểm → hiệu số → bàn thắng); `PlayerStat` bỏ các chỉ số nguồn không có (kiến tạo, sút, chuyền %, tắc/cắt bóng, cứu thua, điểm TB), thêm `Starts`, `PenaltyGoals` (null = không có số liệu), `GoalsConceded`, `TeamId` (đội thi đấu trong giải); `Tournament.HasMatchDetails`; `Player.SourceId`, `AttributesEstimated`; trạng thái “Đã rời giải”.

**KNN – vector 9 chiều mới:** 6 chỉ số năng lực + bàn thắng/90' + tỉ lệ thời gian thi đấu (%) + tỉ lệ giữ sạch lưới (%). Ngưỡng phút tối thiểu mặc định 180'.

**Giao diện:** trang Trận đấu (CRUD), bảng xếp hạng + lịch – kết quả theo vòng (nút số) ở trang giải đấu (quản trị và công khai), trang chủ công khai có bảng xếp hạng, tin tức tự sinh từ dữ liệu thật.

Cập nhật dữ liệu: thay tệp export trong `tools/`, chạy `python tools/build_seed.py`, xóa CSDL rồi chạy lại ứng dụng.

---

## 2g. Chỉ số rõ ràng, trợ lý tra cứu số liệu, tên có dấu

- **Chỉ số chi tiết** (`Services/PlayerInsights.cs`, partial `_AttrInsights`): mỗi chỉ số kèm con số cụ thể.
  Tốc độ hiển thị "≈ km/h" và thời gian chạy 30 m (**quy đổi** từ điểm 1–100, không phải đo GPS);
  dứt điểm = bàn/90 phút, thể lực = phút/trận, phòng ngự = bàn thua/90 & sạch lưới (**số liệu thật**).
  "OVR" đổi thành "Chỉ số tổng (thang 100)" kèm giải thích trọng số theo vị trí.
- **Trợ lý tra cứu số liệu** (`Services/StatsQaService.cs`): hiểu câu hỏi như "Cầu thủ nào ghi nhiều bàn nhất?",
  "Ai thi đấu nhiều phút nhất?", "Đội nào dẫn đầu?", "Kết quả vòng 5", "So sánh A và B", "HLV của HAGL là ai?"…;
  trả lời bằng bảng có liên kết; truy vấn tìm cầu thủ tương tự vẫn dùng KNN.
- **Tên có dấu**: `tools/build_seed.py` khớp tên với danh sách đội hình vi.wikipedia
  (`tools/wikipedia_vi_squads_2026-11-08.txt`); 323/362 cầu thủ Việt Nam có tên đầy đủ dấu, tên không chắc chắn giữ nguyên.

## 2h. Trợ lý AI tự tra cứu CSDL, lọc theo nhu cầu HLV, giá trị theo tỷ VNĐ

- **AI + công cụ tra cứu CSDL (function calling)** – `Services/AiDbTools.cs`, `AiAssistantService.AgentAsync`.
  Khi có khóa API (Claude, Gemini hoặc OpenAI/tương thích), mô hình tự chọn 7 công cụ CHỈ ĐỌC:
  `search_players`, `get_player`, `get_team`, `get_standings`, `get_matches`, `recommend_players` (KNN), `league_overview`;
  máy chủ kiểm tra/giới hạn tham số và truy vấn bằng EF Core (mô hình không viết SQL), tối đa `MaxToolRounds` vòng.
  Khóa API chỉ nằm ở máy chủ. Không có khóa hoặc dịch vụ lỗi → tự chuyển về chế độ tra cứu theo mẫu câu + KNN.
- **Gợi ý KNN & Xếp hạng theo nhu cầu HLV** (`Services/CoachNeeds.cs`): chọn nhu cầu (tốc độ, dứt điểm, chuyền bóng, rê dắt,
  phòng ngự, tì đè, không chiến, bền bỉ, trẻ, kỷ luật; thủ môn: phản xạ, giữ sạch lưới) + điều kiện cứng bằng đơn vị thực
  (chiều cao từ … cm, tốc độ từ … km/h, chân thuận, nội/ngoại binh, giá trị tối đa … tỷ, độ tuổi). Mục "Giữ sạch lưới" chỉ hiện khi chọn Thủ môn.
  Đổi lựa chọn là trang tự cập nhật và cuộn tới kết quả. Vector KNN thêm chiều **chiều cao** (10 chiều; thiếu chiều cao → trung vị nhóm).
- **So sánh cầu thủ**: ô nhập tìm tên (gõ có dấu/không dấu, phím mũi tên + Enter).
- **Giá trị ước tính** hiển thị theo tỷ VNĐ: `max(giá FotMob × 31.000 đ, 3 tỷ × 1,25^(chỉ số tổng − 65) × hệ số tuổi)` (xem `tools/build_seed.py`).
- Hồ sơ cầu thủ bỏ các nhãn "quy đổi / số liệu thật".
- **Chân thuận**: FotMob chỉ có cho ~4% cầu thủ; phần còn lại được **giả lập ngẫu nhiên theo vị trí** (cánh/hậu vệ trái thiên về chân trái),
  cố định theo mã cầu thủ (`foot_of` trong `tools/build_seed.py`). CSDL cũ được tự bổ sung khi khởi động (chỉ điền ô "Chưa rõ").

## 2i. Chịu tải khi nhiều người dùng cùng lúc

- **Bộ nhớ đệm tra cứu dùng chung** (`Services/DataCache.cs`): danh sách cầu thủ + thống kê, bảng xếp hạng, số trận của đội, định giá CLB… chỉ truy vấn CSDL **một lần** cho mọi người đang xem, kể cả khi nhiều người mở trang cùng lúc. Mỗi khi lưu cầu thủ / đội / giải / trận / thống kê / tài khoản, `AppDbContext.SaveChanges` tăng `DataVersion` → dữ liệu mới hiện ngay, không phải đợi.
- **DbContextPool + tự thử lại**: tái sử dụng DbContext giữa các yêu cầu; SQL Server bận hoặc rớt kết nối thoáng qua thì tự thử lại (tối đa 5 lần).
- **Nén phản hồi (Brotli/Gzip)** và **lưu đệm tệp tĩnh** ở trình duyệt (CSS/JS có `?v=` lưu 30 ngày): trang chủ từ ~75 KB còn ~15 KB.
- **Giới hạn tần suất**: trợ lý AI tối đa 15 câu / phút / tài khoản; đăng nhập / đăng ký tối đa 60 lần / phút / địa chỉ IP; một tài khoản nhập sai mật khẩu 5 lần → tạm khóa 5 phút. Có thể đổi trong `appsettings.json`:
  ```json
  "RateLimit": { "AiPerMinute": 15, "LoginPerMinute": 60 }
  ```
- **Nhập liệu cùng lúc**: nếu hai người lưu trùng (VD cùng thêm thống kê một cầu thủ cho một giải) hoặc bản ghi vừa bị người khác xóa, hệ thống quay lại trang trước kèm thông báo thay vì báo lỗi 500 (`Filters/DbConflictFilter.cs`).
- Kiểm thử tải (50 người dùng đồng thời, chỉ tra cứu): ~153 → ~243 yêu cầu/giây, p95 ~540 ms → ~345 ms (máy 2 nhân, CSDL giả lập trong bộ nhớ).

## 4. Cấu trúc thư mục

```
Controllers/   Portal (công khai), Account, Home (Dashboard), Players, Teams, Tournaments, Matches, PlayerStats, Users, Analysis, Recommend, Assistant, News
Data/          AppDbContext (quan hệ, index), DbSeeder (tạo CSDL + nạp dữ liệu V.League), NewsSeeder (tin tự sinh từ dữ liệu), Seed/vleague_seed.json
Models/        AppUser, Team, Tournament, Match, Player, PlayerStat, News, Enums, ViewModels/
tools/         build_seed.py (ETL) + tệp export FotMob gốc
Services/      AuthService (PBKDF2, cookie), AnalysisService (phân tích, xếp hạng, BXH đội), KnnService, AiAssistantService, Helpers
Views/         Giao diện Razor cho từng chức năng + Shared (layout, phân trang, sắp xếp)
wwwroot/       CSS/JS, Bootstrap, Bootstrap Icons (đã đóng gói sẵn, chạy được khi không có mạng)
```

### Sơ đồ quan hệ (rút gọn)

```
Team 1 ──── n Player 1 ──── n PlayerStat n ──── 1 Tournament 1 ──── n Match n ──── 2 Team (nhà/khách)
PlayerStat n ──── 1 Team (đội thi đấu trong giải) · Player.TeamId NULL = tự do / đã rời giải
AppUser (độc lập, Role = Admin | Scout)
```

## 5. Kế hoạch tiếp theo

- Dùng LLM để trích yêu cầu (function calling) thay cho luật từ khóa; ghi nhớ ngữ cảnh hội thoại.
- Danh sách cầu thủ tiềm năng; nhập dữ liệu từ Excel/CSV; xuất PDF; chuyển sang EF Core Migration.
