// Bật/tắt sidebar trên màn hình nhỏ
document.addEventListener('DOMContentLoaded', function () {
  var btn = document.getElementById('sidebarToggle');
  var sb = document.querySelector('.sv-sidebar');
  var bd = document.getElementById('sidebarBackdrop');
  var desktop = function () { return window.matchMedia('(min-width: 992px)').matches; };
  if (btn && sb) btn.addEventListener('click', function () {
    if (!desktop()) { sb.classList.toggle('show'); return; }
    var c = document.documentElement.classList.toggle('sv-collapsed');
    try { localStorage.setItem('sv:sidebar', c ? 'collapsed' : 'open'); } catch (e) { }
    setTimeout(function () { window.dispatchEvent(new Event('resize')); }, 220);   // vẽ lại biểu đồ theo bề rộng mới
  });
  if (bd && sb) bd.addEventListener('click', function () { sb.classList.remove('show'); });
  // Giữ vị trí cuộn của menu khi chuyển trang (không bị nhảy về đầu menu)
  if (sb) {
    var KEY = 'sv:sidebar:scroll';
    try { var y = sessionStorage.getItem(KEY); if (y !== null) sb.scrollTop = +y; } catch (e) { }
    var act = sb.querySelector('.sv-nav a.active');
    if (act) {   // mục đang mở bị khuất thì cuộn tới cho thấy
      var r = act.getBoundingClientRect(), sr = sb.getBoundingClientRect();
      if (r.top < sr.top || r.bottom > sr.bottom) act.scrollIntoView({ block: 'center' });
    }
    var save = function () { try { sessionStorage.setItem(KEY, sb.scrollTop); } catch (e) { } };
    sb.addEventListener('scroll', save, { passive: true });
    window.addEventListener('pagehide', save);
  }
  // Nút trong bảng chỉ có biểu tượng (không có chữ) → ô vuông đều nhau
  document.querySelectorAll('td .btn-sm').forEach(function (b) { if (!b.textContent.trim() && b.querySelector('i.bi')) b.classList.add('btn-icon'); });
  // Khi thu gọn: hiện tên mục khi di chuột vào biểu tượng
  document.querySelectorAll('.sv-nav a').forEach(function (a) { if (!a.title) a.title = (a.firstChild && a.lastChild ? Array.prototype.filter.call(a.childNodes, function (n) { return n.nodeType === 3; }).map(function (n) { return n.textContent; }).join('') : a.textContent).trim(); });

  // Tự gửi form lọc khi đổi lựa chọn trong dropdown
  document.querySelectorAll('form.auto-submit select').forEach(function (el) {
    el.addEventListener('change', function () { el.form.submit(); });
  });

  // Hiển thị giá trị thanh trượt chỉ số
  document.querySelectorAll('input[type=range][data-output]').forEach(function (el) {
    var out = document.getElementById(el.dataset.output);
    var update = function () { if (out) out.textContent = el.value; };
    el.addEventListener('input', update); update();
  });
});

// =====================================================================
// Chuyển trang nội dung bằng nút số (thay cho cuộn trang dài)
//  1) <div class="sv-paged" data-start="2"> <section class="sv-page" data-title="...">…</section> … </div>
//     → mỗi lần chỉ hiện một phần; thanh nút số ở trên và dưới.
//  2) <table data-page-size="10"> → chia dòng của bảng thành nhiều trang.
// =====================================================================
(function () {
  function pageNumbers(cur, total) {
    // 1 … 4 5 [6] 7 8 … 20
    var list = [], from = Math.max(1, cur - 2), to = Math.min(total, cur + 2);
    if (from > 1) { list.push(1); if (from > 2) list.push('…'); }
    for (var i = from; i <= to; i++) list.push(i);
    if (to < total) { if (to < total - 1) list.push('…'); list.push(total); }
    return list;
  }
  function pagination(cur, total, go, label) {
    var ul = document.createElement('ul');
    ul.className = 'pagination pagination-sm mb-0';
    function item(text, page, opts) {
      opts = opts || {};
      var li = document.createElement('li');
      li.className = 'page-item' + (opts.active ? ' active' : '') + (opts.disabled ? ' disabled' : '');
      var b = document.createElement(opts.disabled && text === '…' ? 'span' : 'button');
      b.className = 'page-link'; b.type = 'button'; b.innerHTML = text;
      if (opts.title) b.title = opts.title;
      if (opts.active) b.setAttribute('aria-current', 'page');
      if (!opts.disabled && !opts.active && b.tagName === 'BUTTON') b.addEventListener('click', function () { go(page); });
      li.appendChild(b); ul.appendChild(li);
    }
    item('&laquo;', cur - 1, { disabled: cur <= 1, title: 'Trang trước' });
    pageNumbers(cur, total).forEach(function (n) {
      if (n === '…') item('…', 0, { disabled: true });
      else item(String(n), n, { active: n === cur, title: label ? label(n) : 'Trang ' + n });
    });
    item('&raquo;', cur + 1, { disabled: cur >= total, title: 'Trang sau' });
    return ul;
  }

  // ----- 1) Phân đoạn nội dung -----
  function initPaged(box, idx) {
    var pages = Array.prototype.filter.call(box.children, function (c) { return c.classList.contains('sv-page'); });
    if (pages.length < 2) { pages.forEach(function (p) { p.classList.add('active'); }); return; }
    var titles = pages.map(function (p, i) { return p.dataset.title || ('Phần ' + (i + 1)); });
    var hashKey = 'trang' + (idx ? idx + 1 : '');
    var top = document.createElement('nav'); top.className = 'sv-steps'; top.setAttribute('aria-label', 'Chuyển nội dung');
    var bottom = document.createElement('nav'); bottom.className = 'sv-steps-bottom'; bottom.setAttribute('aria-label', 'Chuyển nội dung');
    box.insertBefore(top, pages[0]); box.appendChild(bottom);
    var cur = 0;
    var compact = box.dataset.compact === '1' || box.hasAttribute('data-nohash');
    var useHash = !box.hasAttribute('data-nohash');

    function show(n, scroll) {
      n = Math.max(1, Math.min(pages.length, n));
      cur = n;
      pages.forEach(function (p, i) { p.classList.toggle('active', i === n - 1); });
      if (compact) {
        // Chế độ gọn (nhiều trang, ví dụ lịch thi đấu theo vòng): tiêu đề + nút số ở trên
        top.innerHTML = ''; top.className = 'sv-steps-compact';
        var lbl = document.createElement('div'); lbl.className = 'fw-semibold'; lbl.textContent = titles[n - 1] + ' / ' + pages.length;
        top.appendChild(lbl);
        top.appendChild(pagination(n, pages.length, function (p) { show(p, false); }, function (p) { return titles[p - 1]; }));
        bottom.style.display = 'none';
        return;
      }
      // Thanh trên: nút số kèm tên phần
      top.innerHTML = '';
      titles.forEach(function (t, i) {
        var b = document.createElement('button');
        b.type = 'button'; b.className = 'sv-step' + (i === n - 1 ? ' active' : '');
        b.innerHTML = '<span class="num">' + (i + 1) + '</span><span class="txt">' + t + '</span>';
        if (i === n - 1) b.setAttribute('aria-current', 'page');
        b.addEventListener('click', function () { show(i + 1, true); });
        top.appendChild(b);
      });
      // Thanh dưới: « 1 2 3 » + tên phần kế tiếp
      bottom.innerHTML = '';
      var info = document.createElement('div'); info.className = 'small text-muted';
      info.textContent = 'Phần ' + n + '/' + pages.length + ': ' + titles[n - 1];
      bottom.appendChild(info);
      bottom.appendChild(pagination(n, pages.length, function (p) { show(p, true); }, function (p) { return titles[p - 1]; }));
      if (useHash) try { history.replaceState(null, '', '#' + hashKey + '-' + n); } catch (e) { }
      if (scroll) {
        var y = box.getBoundingClientRect().top + window.scrollY - 70;
        if (window.scrollY > y) window.scrollTo({ top: y, behavior: 'smooth' });
      }
      // Biểu đồ trong phần vừa hiện cần đo lại kích thước
      window.dispatchEvent(new Event('resize'));
    }
    var m = useHash ? location.hash.match(new RegExp('^#' + hashKey + '-(\\d+)$')) : null;
    show(m ? +m[1] : (+box.dataset.start || 1), false);
  }

  // ----- 2) Phân trang bảng -----
  function initTable(table, idx) {
    var size = +table.dataset.pageSize || 10;
    // Nhớ trang đang xem của bảng (quay lại trang vẫn ở đúng trang cũ)
    var memKey = 'sv:tp:' + location.pathname + location.search + ':' + idx;
    var tbody = table.tBodies[0]; if (!tbody) return;
    var rows = Array.prototype.slice.call(tbody.rows);
    if (rows.length <= size) return;
    var total = Math.ceil(rows.length / size);
    var holder = table.closest('.table-responsive') || table;
    var bar = document.createElement('div');
    bar.className = 'sv-table-pager d-flex flex-wrap justify-content-between align-items-center gap-2 px-3 py-2';
    holder.parentNode.insertBefore(bar, holder.nextSibling);
    function show(n) {
      rows.forEach(function (r, i) { r.style.display = (i >= (n - 1) * size && i < n * size) ? '' : 'none'; });
      bar.innerHTML = '';
      var info = document.createElement('div'); info.className = 'small text-muted';
      info.textContent = 'Hiển thị ' + ((n - 1) * size + 1) + '–' + Math.min(n * size, rows.length) + ' / ' + rows.length + ' dòng';
      bar.appendChild(info);
      bar.appendChild(pagination(n, total, show));
      try { sessionStorage.setItem(memKey, n); } catch (e) { }
    }
    var start = 1;
    try { start = Math.min(total, Math.max(1, +sessionStorage.getItem(memKey) || 1)); } catch (e) { }
    show(start);
  }

  document.addEventListener('DOMContentLoaded', function () {
    var k = 0;
    document.querySelectorAll('.sv-paged').forEach(function (b) { initPaged(b, b.hasAttribute('data-nohash') ? 0 : k++); });
    document.querySelectorAll('table[data-page-size]').forEach(initTable);
  });
})();

// =====================================================================
// Ô nhập tìm cầu thủ (gõ tên có dấu/không dấu → danh sách gợi ý) và tự áp dụng bộ lọc
// =====================================================================
(function () {
  function plain(s) {
    return (s || '').normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase();
  }
  function esc(s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }

  function initPicker(box) {
    var input = box.querySelector('.pp-input'), hidden = box.querySelector('.pp-value');
    var menu = box.querySelector('.pp-menu'), clear = box.querySelector('.pp-clear');
    var list = (window.SVPlayers || []).map(function (p) { p.key = plain(p.name + ' ' + p.info); return p; });
    var active = -1, shown = [];

    function close() { menu.innerHTML = ''; menu.style.display = 'none'; active = -1; }
    function choose(p) {
      input.value = p.name; hidden.value = p.id; close();
      hidden.dispatchEvent(new Event('change', { bubbles: true }));
    }
    function render() {
      var q = plain(input.value).trim();
      if (!q) { close(); return; }
      var words = q.split(/\s+/);
      shown = list.filter(function (p) { return words.every(function (w) { return p.key.indexOf(w) >= 0; }); }).slice(0, 8);
      menu.innerHTML = shown.length ? '' : '<div class="list-group-item small text-muted">Không tìm thấy cầu thủ</div>';
      shown.forEach(function (p, i) {
        var a = document.createElement('button');
        a.type = 'button'; a.className = 'list-group-item list-group-item-action py-1' + (i === active ? ' active' : '');
        a.innerHTML = '<div class="fw-semibold small">' + esc(p.name) + '</div><div class="small text-muted">' + esc(p.info) + '</div>';
        a.addEventListener('mousedown', function (e) { e.preventDefault(); choose(p); });
        menu.appendChild(a);
      });
      menu.style.display = 'block';
    }
    input.addEventListener('input', function () { hidden.value = ''; active = -1; render(); });
    input.addEventListener('focus', render);
    input.addEventListener('blur', function () { setTimeout(close, 150); });
    input.addEventListener('keydown', function (e) {
      if (menu.style.display !== 'block' || !shown.length) return;
      if (e.key === 'ArrowDown') { active = Math.min(shown.length - 1, active + 1); render(); e.preventDefault(); }
      else if (e.key === 'ArrowUp') { active = Math.max(0, active - 1); render(); e.preventDefault(); }
      else if (e.key === 'Enter') { choose(shown[Math.max(0, active)]); e.preventDefault(); }
      else if (e.key === 'Escape') close();
    });
    clear.addEventListener('click', function () { input.value = ''; hidden.value = ''; input.focus(); close(); });
  }

  // Form lọc (class "auto-form"): chỉ gửi khi bấm nút; đổi vị trí → ẩn/hiện nhu cầu riêng của thủ môn
  function initAutoForm(form) {
    var group = form.querySelector('select[name="group"]');
    function syncNeeds() {
      if (!group) return;
      var gk = group.value === 'GK';
      form.querySelectorAll('.need-item').forEach(function (it) {
        var sc = it.dataset.scope, hide = gk ? sc === 'out' : sc === 'gk';
        it.hidden = hide;
        var cb = it.querySelector('input'); cb.disabled = hide; if (hide) cb.checked = false;
      });
    }
    if (group) group.addEventListener('change', syncNeeds);
    var err = form.querySelector('.need-error');
    form.querySelectorAll('.need-item input').forEach(function (cb) {
      cb.addEventListener('change', function () { if (err && form.querySelector('.need-item input:checked')) err.hidden = true; });
    });
    form.addEventListener('submit', function (e) {
      // Chưa chọn tiêu chí nào → báo, không gửi
      if (err && !form.querySelector('.need-item:not([hidden]) input:checked')) {
        e.preventDefault();
        err.hidden = false;
        // Chưa có tiêu chí thì phần kết quả cũng để trống
        var res = document.getElementById('ket-qua');
        var tpl = document.getElementById('emptyResult');
        if (res && tpl) res.innerHTML = tpl.innerHTML;
        var chart = document.getElementById('bieu-do');
        if (chart && tpl) chart.innerHTML = tpl.innerHTML;
        document.querySelectorAll('.export-btn').forEach(function (b) { b.remove(); });
        err.scrollIntoView({ behavior: 'smooth', block: 'center' });
        return;
      }
      // Bỏ các ô trống để địa chỉ trang gọn
      form.querySelectorAll('input, select').forEach(function (el) { if (el.name && el.value === '' && el.type !== 'checkbox') el.disabled = true; });
    });
  }

  // Bảng kết quả: tick chọn tối đa N cầu thủ rồi mở trang So sánh
  function initCompareBar(bar) {
    var max = +bar.dataset.max || 4, fixed = bar.dataset.fixed, tid = bar.dataset.tid;
    var scope = bar.closest('section') || document;
    var boxes = Array.prototype.slice.call(scope.querySelectorAll('.cmp-pick'));
    var count = bar.querySelector('.cmp-count'), go = bar.querySelector('.cmp-go');
    function sync() {
      var n = boxes.filter(function (b) { return b.checked; }).length;
      count.textContent = n;
      boxes.forEach(function (b) { b.disabled = !b.checked && n >= max; });
      go.disabled = n + (fixed ? 1 : 0) < 2;
    }
    boxes.forEach(function (b) { b.addEventListener('change', sync); });
    go.addEventListener('click', function () {
      var ids = boxes.filter(function (b) { return b.checked; }).map(function (b) { return b.value; });
      if (fixed) ids.unshift(fixed);
      location.href = '/Analysis/Compare?' + ids.map(function (i) { return 'ids=' + i; }).join('&') + (tid ? '&tournamentId=' + tid : '');
    });
    sync();
  }

  // Quay lại trang (bfcache): bật lại các ô đã tắt khi gửi form
  window.addEventListener('pageshow', function () {
    document.querySelectorAll('form.auto-form [disabled]').forEach(function (el) { if (!el.closest('.need-item[hidden]')) el.disabled = false; });
    var r = document.getElementById('ket-qua'); if (r) r.classList.remove('loading');
  });

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.player-picker').forEach(initPicker);
    document.querySelectorAll('.cmp-bar').forEach(initCompareBar);
    document.querySelectorAll('form.auto-form').forEach(initAutoForm);
  });
})();

// =====================================================================
// Giữ nguyên bộ lọc / trang khi quay lại danh sách sau khi xem, sửa, thêm, xóa
//  - Trang danh sách: lưu địa chỉ đầy đủ (kèm bộ lọc, trang) vào sessionStorage.
//  - Nút "Danh sách" / "Hủy" trỏ về danh sách trống → đổi thành địa chỉ đã lưu.
//  - Sau khi lưu / xóa, máy chủ chuyển về danh sách trống → tự chuyển sang địa chỉ đã lưu (giữ thông báo).
//  - Bảng chia trang phía trình duyệt nhớ trang đang xem.
// =====================================================================
(function () {
  var LISTS = ['/players', '/teams', '/tournaments', '/matches', '/news', '/playerstats', '/users'];
  function norm(p) { return (p || '').split(/[?#]/)[0].replace(/\/index$/i, '').replace(/\/$/, '').toLowerCase() || '/'; }
  function get(k) { try { return sessionStorage.getItem(k); } catch (e) { return null; } }
  function set(k, v) { try { sessionStorage.setItem(k, v); } catch (e) { } }
  function del(k) { try { sessionStorage.removeItem(k); } catch (e) { } }
  var path = norm(location.pathname);

  // Thông báo được chuyển tiếp từ lần chuyển trang trước
  var flash = get('sv:flash');
  if (flash) {
    del('sv:flash');
    document.addEventListener('DOMContentLoaded', function () {
      var main = document.querySelector('.sv-content');
      if (main) main.insertAdjacentHTML('afterbegin', flash);
    });
  }

  if (LISTS.indexOf(path) >= 0) {
    var saved = get('sv:list:' + path);
    var ref = '';
    try { ref = norm(new URL(document.referrer).pathname); } catch (e) { }
    var fromCrud = ref.indexOf(path + '/') === 0 && /\/(edit|create|delete)(\/|$)/.test(ref);
    if (!location.search && fromCrud && saved && saved.indexOf('?') > 0) {
      // Vừa lưu / xóa xong: quay về đúng bộ lọc, trang cũ
      document.addEventListener('DOMContentLoaded', function () {
        var alerts = document.querySelectorAll('.sv-content > .alert');
        if (alerts.length) set('sv:flash', Array.prototype.map.call(alerts, function (a) { return a.outerHTML; }).join(''));
        location.replace(saved);
      });
    } else {
      set('sv:list:' + path, location.pathname + location.search);
    }
  }

  document.addEventListener('DOMContentLoaded', function () {
    // Nút quay về danh sách (không áp dụng cho nút "Xóa lọc" trong form lọc)
    document.querySelectorAll('.sv-content a[href]').forEach(function (a) {
      var h = a.getAttribute('href');
      if (!h || h.indexOf('?') >= 0 || h.indexOf('#') >= 0) return;
      var p = norm(h);
      if (LISTS.indexOf(p) < 0 || p === path) return;
      if (a.closest('form[method="get"], form.auto-submit')) return;
      var s = get('sv:list:' + p);
      if (s) a.setAttribute('href', s);
    });

  });
})();
