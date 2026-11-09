using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using ScoutVN.Models.ViewModels;
using ScoutVN.Services;

namespace ScoutVN.Controllers
{
    /// <summary>
    /// Gợi ý cầu thủ bằng KNN theo nhu cầu chuyên môn của HLV (tốc độ, tì đè, không chiến, giữ sạch lưới...)
    /// hoặc tìm cầu thủ tương tự một cầu thủ mẫu. Bộ lọc cứng dùng đơn vị thực: cm, km/h, tỷ VNĐ.
    /// </summary>
    [Authorize]
    public class RecommendController : Controller
    {
        private readonly IAnalysisService _analysis;
        private readonly IKnnService _knn;

        public RecommendController(IAnalysisService analysis, IKnnService knn)
        {
            _analysis = analysis;
            _knn = knn;
        }

        // GET /Recommend?mode=criteria|similar&group=ATT&n=speed&n=aerial&minHeight=180&minSpeed=31&...
        public async Task<IActionResult> Index(string? mode, PositionGroup? group, int? tournamentId,
            [FromQuery(Name = "n")] string[]? needs, int? minHeight, double? minSpeed, string? foot, string? origin, double? maxValue,
            int? minAge, int? maxAge, int minMinutes = KnnQuery.DefaultMinMinutes, int k = 10, int? playerId = null, string? format = null)
        {
            var vm = new RecommendViewModel
            {
                Mode = mode == "similar" ? "similar" : "criteria",
                MinAge = minAge, MaxAge = maxAge,
                MinMinutes = Math.Clamp(minMinutes, 0, 5000),
                K = Math.Clamp(k, 1, 50),
                PlayerId = playerId,
                Features = _knn.Features
            };
            // Lần đầu vào trang: mặc định tìm tiền đạo
            if (!Request.Query.Any()) group = PositionGroup.ATT;

            var list = await _analysis.TournamentsWithStatsAsync();
            int tid = tournamentId ?? await _analysis.DefaultTournamentIdAsync() ?? 0;
            if (tid != 0 && list.All(x => x.Id != tid)) tid = 0;
            vm.TournamentId = tid;
            vm.Tournaments = new List<SelectListItem> { new("Tất cả giải (tổng hợp)", "0", tid == 0) };
            vm.Tournaments.AddRange(list.Select(x => new SelectListItem($"{x.Name} {x.Season}", x.Id.ToString(), x.Id == tid)));
            vm.ScopeName = vm.Tournaments.First(i => i.Selected).Text;

            var withStats = (await _analysis.LoadRowsAsync(tid)).Where(r => r.Stat != null)
                .OrderBy(r => r.Player.FullName).ToList();
            vm.Picks = withStats.Select(r => new PlayerPick(r.Player.Id, r.Player.FullName,
                $"{r.Player.Position} · {r.Player.Team?.ShortName ?? "Tự do"} · {r.Player.Age} tuổi")).ToList();

            var query = new KnnQuery { TournamentId = tid, MinAge = minAge, MaxAge = maxAge, MinMinutes = vm.MinMinutes, K = vm.K };
            if (vm.Mode == "similar")
            {
                vm.Sample = withStats.FirstOrDefault(r => r.Player.Id == playerId);
                if (vm.Sample != null)
                {
                    group ??= PositionGroups.Of(vm.Sample.Player.Position);
                    query.ExcludePlayerId = vm.Sample.Player.Id;
                }
            }
            vm.Group = group;
            // Đã bấm "Xem gợi ý" (go=1) mà chưa chọn tiêu chí nào → báo lỗi, không dùng bộ mặc định
            vm.NeedsMissing = Request.Query.ContainsKey("go") && CoachNeeds.Valid(needs, group).Count == 0;
            // Không chọn sẵn tiêu chí nào: HLV tự chọn (bấm "Bỏ lọc" cũng trở về trạng thái chưa chọn)
            vm.Needs = vm.NeedsMissing ? new List<string>() : CoachNeeds.Valid(needs, group);
            vm.Filter = new NeedFilter
            {
                Needs = vm.Needs,
                MinHeight = minHeight is > 0 ? Math.Clamp(minHeight.Value, 150, 210) : null,
                MinSpeed = minSpeed is > 0 ? Math.Clamp(minSpeed.Value, 24, 36) : null,
                Foot = foot is "Left" or "Right" ? foot : null,
                Origin = origin is "vn" or "foreign" ? origin : null,
                MaxValue = maxValue is > 0 ? maxValue : null,
                MinAge = minAge, MaxAge = maxAge
            };
            // Chưa bấm "Xem gợi ý" hoặc chưa chọn tiêu chí → chưa tính kết quả
            if (vm.NeedsMissing || !Request.Query.ContainsKey("go")) return View(vm);
            // "Trẻ, tiềm năng" trong KNN = điều kiện tuổi ≤ 23 (nếu HLV chưa đặt tuổi)
            if (vm.Needs.Contains("young") && maxAge == null) { query.MaxAge = 23; vm.Filter.MaxAge = 23; }
            vm.Columns = CoachNeeds.Columns(vm.Needs);
            if (vm.Mode == "similar" && vm.Sample == null) return View(vm);

            vm.Weights = KnnService.WeightsFromNeeds(vm.Needs);
            if (vm.Weights.Values.Sum() <= 0)   // chỉ chọn "Trẻ" / "Kỷ luật" → thêm nhu cầu mặc định của vị trí
                vm.Weights = KnnService.WeightsFromNeeds(vm.Needs.Concat(CoachNeeds.Defaults(group)));
            vm.Target = vm.Sample != null ? _knn.VectorOf(vm.Sample) : await _knn.SuggestTargetAsync(group, tid, vm.MinMinutes);

            query.Group = group;
            query.Target = vm.Target;
            query.Weights = vm.Weights;
            query.Filter = vm.Filter;
            vm.Result = await _knn.RecommendAsync(query);
            if (format == "csv") return Csv(vm);
            return View(vm);
        }

        /// <summary>Xuất kết quả gợi ý ra CSV (mở bằng Excel) – cột theo đơn vị thực của các tiêu chí đã chọn.</summary>
        private FileContentResult Csv(RecommendViewModel vm)
        {
            static string C(string? s) { s ??= ""; return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
            var sb = new System.Text.StringBuilder();
            var crit = string.Join("; ", vm.Needs.Select(k => CoachNeeds.Find(k)?.Name));
            sb.AppendLine(C($"Gợi ý cầu thủ (KNN) – {vm.ScopeName} – " + (vm.Sample != null ? $"giống {vm.Sample.Player.FullName} – " : "") + $"tiêu chí: {crit}"));
            sb.Append("Hạng,Cầu thủ,Đội,Vị trí,Tuổi,Phù hợp (%)");
            foreach (var c in vm.Columns) sb.Append(',').Append(C(c.Header));
            sb.AppendLine(",Mạnh,Còn thiếu");
            int i = 1;
            foreach (var n in vm.Result!.Neighbors)
            {
                var p = n.Row.Player;
                sb.Append($"{i++},{C(p.FullName)},{C(p.Team?.Name ?? "Tự do")},{p.Position},{p.Age},{n.Match.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}");
                foreach (var c in vm.Columns) sb.Append(',').Append(C(c.Show(n.Row)));
                sb.AppendLine($",{C(string.Join("; ", n.Strengths))},{C(string.Join("; ", n.Gaps))}");
            }
            var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"GoiY_KNN_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }
    }
}
