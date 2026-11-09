// ===== ScoutVN – cấu hình biểu đồ dùng chung (Chart.js 4) =====
// Bảng màu phân loại cố định thứ tự (không xoay vòng), đã kiểm tra khả năng phân biệt cho người mù màu.
window.SV = (function () {
  const palette = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100'];
  const neutral = '#8a8984';
  const isDark = () => document.documentElement.getAttribute('data-theme') === 'dark';
  const text = isDark() ? '#b8c9bf' : '#52514e';
  const grid = isDark() ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.07)';
  // Mã hóa phụ (ngoài màu): kiểu điểm và nét đứt khác nhau cho từng chuỗi
  const pointStyles = ['circle', 'rect', 'triangle', 'rectRot'];
  const dashes = [[], [6, 4], [2, 3], [10, 3, 2, 3]];

  if (window.Chart) {
    Chart.defaults.font.family = '"Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';
    Chart.defaults.font.size = 12;
    Chart.defaults.color = text;
    Chart.defaults.plugins.legend.labels.usePointStyle = true;
    Chart.defaults.plugins.tooltip.backgroundColor = 'rgba(20,20,20,0.9)';
    Chart.defaults.plugins.tooltip.padding = 8;
    Chart.defaults.maintainAspectRatio = false;
  }

  function hexA(hex, a) {
    const n = parseInt(hex.slice(1), 16);
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${a})`;
  }

  const fmt = (v, d) => (v == null ? '–' : Number(v).toLocaleString('vi-VN', { maximumFractionDigits: d ?? 2 }));

  /** Biểu đồ cột (dọc hoặc ngang) một chuỗi. links: mảng URL để click vào cột mở trang chi tiết. */
  function bar(id, labels, values, opts = {}) {
    const el = document.getElementById(id);
    if (!el) return null;
    const horizontal = !!opts.horizontal;
    const chart = new Chart(el, {
      type: 'bar',
      data: {
        labels,
        datasets: [{
          label: opts.label || '',
          data: values,
          backgroundColor: opts.color || palette[0],
          hoverBackgroundColor: hexA(opts.color || palette[0], 0.8),
          borderRadius: 4,
          borderSkipped: 'start',
          maxBarThickness: 26,
          categoryPercentage: 0.8,
          barPercentage: 0.9
        }]
      },
      options: {
        indexAxis: horizontal ? 'y' : 'x',
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (c) => `${opts.label || ''}: ${fmt(c.raw, opts.decimals)}${opts.unit || ''}` } }
        },
        scales: {
          [horizontal ? 'x' : 'y']: { beginAtZero: opts.min == null, min: opts.min, max: opts.max, grid: { color: grid }, ticks: { precision: opts.decimals ? undefined : 0 } },
          [horizontal ? 'y' : 'x']: { grid: { display: false }, ticks: { autoSkip: false } }
        },
        onClick: (e, els) => { if (opts.links && els.length) window.location = opts.links[els[0].index]; },
        onHover: (e, els) => { if (opts.links) e.native.target.style.cursor = els.length ? 'pointer' : 'default'; }
      }
    });
    return chart;
  }

  /** Biểu đồ cột nhóm (nhiều chuỗi cùng đơn vị). */
  function groupedBar(id, labels, series, opts = {}) {
    const el = document.getElementById(id);
    if (!el) return null;
    return new Chart(el, {
      type: 'bar',
      data: {
        labels,
        datasets: series.map((s, i) => ({
          label: s.name, data: s.values,
          backgroundColor: s.color || palette[i % palette.length],
          borderColor: '#fff', borderWidth: { top: 0, bottom: 0, left: 1, right: 1 },
          borderRadius: 4, borderSkipped: 'start', maxBarThickness: 22,
          pointStyle: pointStyles[i]
        }))
      },
      options: {
        indexAxis: opts.horizontal ? 'y' : 'x',
        plugins: {
          legend: { position: 'bottom' },
          tooltip: { mode: 'index', intersect: false, callbacks: { label: (c) => `${c.dataset.label}: ${fmt(c.raw, opts.decimals)}` } }
        },
        scales: {
          [opts.horizontal ? 'x' : 'y']: { beginAtZero: true, grid: { color: grid }, ticks: { precision: opts.decimals ? undefined : 0 } },
          [opts.horizontal ? 'y' : 'x']: { grid: { display: false } }
        }
      }
    });
  }

  /** Biểu đồ radar 0–100. series: [{name, values, dashed?, color?}] */
  function radar(id, labels, series) {
    const el = document.getElementById(id);
    if (!el) return null;
    return new Chart(el, {
      type: 'radar',
      data: {
        labels,
        datasets: series.map((s, i) => {
          const color = s.color || palette[i % palette.length];
          return {
            label: s.name, data: s.values,
            borderColor: color, backgroundColor: hexA(color, s.fill === false ? 0 : 0.12),
            borderWidth: 2, borderDash: s.dashed ? [6, 4] : dashes[i % dashes.length],
            pointBackgroundColor: color, pointBorderColor: '#fff', pointBorderWidth: 2,
            pointRadius: 4, pointHoverRadius: 6, pointStyle: s.pointStyle || pointStyles[i % pointStyles.length]
          };
        })
      },
      options: {
        plugins: { legend: { position: 'bottom' }, tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${fmt(c.raw, 1)}` } } },
        scales: { r: { min: 0, max: 100, ticks: { stepSize: 20, backdropColor: 'transparent', color: '#8a8984' }, grid: { color: grid }, angleLines: { color: grid }, pointLabels: { font: { size: 12, weight: '600' } } } }
      }
    });
  }

  /** Biểu đồ đường một hoặc nhiều chuỗi cùng đơn vị. */
  function line(id, labels, series, opts = {}) {
    const el = document.getElementById(id);
    if (!el) return null;
    return new Chart(el, {
      type: 'line',
      data: {
        labels,
        datasets: series.map((s, i) => {
          const color = s.color || palette[i % palette.length];
          return { label: s.name, data: s.values, borderColor: color, backgroundColor: color, borderWidth: 2, tension: 0.25, pointRadius: 4, pointHoverRadius: 6, pointStyle: pointStyles[i] };
        })
      },
      options: {
        interaction: { mode: 'index', intersect: false },
        plugins: { legend: { display: series.length > 1, position: 'bottom' }, tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${fmt(c.raw, opts.decimals)}` } } },
        scales: { y: { min: opts.min, max: opts.max, grid: { color: grid } }, x: { grid: { display: false } } }
      }
    });
  }

  /** Đổi màu chữ / lưới của mọi biểu đồ khi chuyển sáng ↔ tối (không cần tải lại trang). */
  function retheme() {
    if (!window.Chart) return;
    const t = isDark() ? '#b8c9bf' : '#52514e', g = isDark() ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.07)';
    Chart.defaults.color = t;
    Object.values(Chart.instances || {}).forEach(function (c) {
      Object.values(c.options.scales || {}).forEach(function (sc) {
        if (sc.ticks) sc.ticks.color = sc.type === 'radialLinear' ? '#8a8984' : t;
        if (sc.pointLabels) sc.pointLabels.color = t;
        if (sc.grid && sc.grid.display !== false) sc.grid.color = g;
        if (sc.angleLines) sc.angleLines.color = g;
      });
      if (c.options.plugins && c.options.plugins.legend && c.options.plugins.legend.labels) c.options.plugins.legend.labels.color = t;
      c.update('none');
    });
  }

  return { palette, neutral, bar, groupedBar, radar, line, fmt, retheme };
})();
