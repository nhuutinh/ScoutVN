// ===== ScoutVN – chuyển giao diện sáng / tối =====
// Lưu lựa chọn trong localStorage "sv:theme"; chưa chọn thì theo cài đặt của máy.
// Hiệu ứng: vòng tròn lan ra từ nút bấm (View Transitions API – GPU, không bị giật);
// trình duyệt chưa hỗ trợ thì đổi ngay lập tức.
(function () {
  var KEY = 'sv:theme', root = document.documentElement;
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;
  function saved() { try { return localStorage.getItem(KEY); } catch (e) { return null; } }
  function current() { return root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'; }

  function apply(t) {
    root.setAttribute('data-theme', t);
    document.querySelectorAll('.theme-toggle').forEach(function (b) {
      var label = t === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối';
      b.title = label; b.setAttribute('aria-label', label); b.setAttribute('aria-pressed', t === 'dark');
    });
    if (window.SV && SV.retheme) SV.retheme();
  }

  // Đổi giao diện không có hiệu ứng chuyển màu từng phần tử (tránh giật)
  function applyInstant(t) {
    root.classList.add('theme-switching');
    apply(t);
    void root.offsetWidth;   // áp dụng màu mới ngay trong khung hình này
    requestAnimationFrame(function () { requestAnimationFrame(function () { root.classList.remove('theme-switching'); }); });
  }

  function switchTo(t, origin) {
    if (t === current()) return;
    if (!document.startViewTransition || reduce || document.hidden) { applyInstant(t); return; }
    var x = innerWidth - 40, y = 40;
    if (origin) { var r = origin.getBoundingClientRect(); x = r.left + r.width / 2; y = r.top + r.height / 2; }
    var rad = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));
    root.classList.add('theme-switching');
    var vt = document.startViewTransition(function () { apply(t); });
    vt.ready.then(function () {
      root.animate(
        { clipPath: ['circle(0px at ' + x + 'px ' + y + 'px)', 'circle(' + rad + 'px at ' + x + 'px ' + y + 'px)'] },
        { duration: 480, easing: 'cubic-bezier(.4, 0, .2, 1)', pseudoElement: '::view-transition-new(root)' });
    }).catch(function () { });
    vt.finished.finally(function () { root.classList.remove('theme-switching'); });
  }

  document.addEventListener('click', function (e) {
    var b = e.target.closest && e.target.closest('.theme-toggle'); if (!b) return;
    var t = current() === 'dark' ? 'light' : 'dark';
    try { localStorage.setItem(KEY, t); } catch (err) { }
    switchTo(t, b);
  });
  if (window.matchMedia) {
    var mq = matchMedia('(prefers-color-scheme: dark)');
    var onChange = function (e) { if (!saved()) switchTo(e.matches ? 'dark' : 'light'); };
    if (mq.addEventListener) mq.addEventListener('change', onChange); else if (mq.addListener) mq.addListener(onChange);
  }
  // Đồng bộ giữa các tab đang mở
  window.addEventListener('storage', function (e) { if (e.key === KEY && (e.newValue === 'dark' || e.newValue === 'light')) applyInstant(e.newValue); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { apply(current()); }); else apply(current());
})();
