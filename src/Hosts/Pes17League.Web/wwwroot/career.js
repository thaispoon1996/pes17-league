// UI Career đợt 1 (Stitch "Nocturnal Apex", PO 19/11/2026): bụi sáng, đếm số, pháo giấy khi thắng, hiệu ứng vào trang. Chỉ chạy khi có .cx.
(() => {
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  // tiền giống MoneyText.Format phía server (PO 20/11): €8.15M, €35.5K, €1.25B; bậc lấy theo số ĐÍCH để lúc đếm không nhảy K↔M
  const UNITS = [[1e3, 'K', 1], [1e6, 'M', 2], [1e9, 'B', 2]];
  const unitOf = a => a < 1000 ? null : UNITS.find(([u, s, d]) => +(a / u).toFixed(d) < 1000 || s === 'B');
  const money = (v, to) => { const a = Math.abs(v), u = unitOf(Math.abs(to ?? v));
    return (v < 0 ? '-€' : '€') + (u ? String(+(a / u[0]).toFixed(u[2])) + u[1] : String(Math.round(a))); };
  const fmt = (v, kind, to) => kind === 'money' ? money(v, to) : String(Math.round(v));

  // đếm số: <b data-count="123" data-fmt="money">
  const counted = new WeakMap();
  function count(el) {
    const to = Number(el.dataset.count);
    if (!Number.isFinite(to) || counted.get(el) === to) return;
    const from = counted.has(el) ? counted.get(el) : (el.dataset.fmt === 'money' ? to : 0);
    counted.set(el, to);
    if (reduce || from === to) { el.textContent = fmt(to, el.dataset.fmt, to); return; }
    const t0 = performance.now(), dur = 900;
    const step = now => {
      const k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
      el.textContent = fmt(from + (to - from) * e, el.dataset.fmt, to);
      if (k < 1 && counted.get(el) === to) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  // đang cuộn → <html data-cx-scroll> (career.css dừng hiệu ứng lặp), ngừng ~150ms thì bỏ. Không dùng class: MutationObserver dưới theo dõi class.
  // capture: sự kiện cuộn của phần tử (bảng chi tiết giải/CLB…) không nổi bọt lên window → phải bắt ở pha capture mới thấy (PO 20/11).
  let scrolling = false, scrollT;
  addEventListener('scroll', () => {
    if (!scrolling && document.querySelector('.cx')) { scrolling = true; document.documentElement.dataset.cxScroll = ''; }
    clearTimeout(scrollT);
    scrollT = setTimeout(() => { scrolling = false; delete document.documentElement.dataset.cxScroll; }, 150);
  }, { passive: true, capture: true });

  // bụi sáng trên nền
  let dust = null;
  function startDust(canvas) {
    if (dust?.canvas === canvas || reduce) return;
    dust?.stop();
    const ctx = canvas.getContext('2d');
    let w, h, alive = true;
    const resize = () => { w = canvas.width = innerWidth; h = canvas.height = innerHeight; };
    resize(); addEventListener('resize', resize);
    const ps = Array.from({ length: 60 }, () => ({ x: Math.random(), y: Math.random(), r: Math.random() * 1.6 + .3, v: Math.random() * .00025 + .00006, a: Math.random() * Math.PI * 2,
      c: Math.random() < .2 ? '255,201,74' : Math.random() < .5 ? '54,227,255' : '220,230,255' }));
    const sprites = {};   // hạt có quầng sáng vẽ sẵn 1 lần → mỗi khung chỉ drawImage
    for (const p of ps) if (!sprites[p.c]) {
      const c = document.createElement('canvas'); c.width = c.height = 24; const g = c.getContext('2d');
      const gr = g.createRadialGradient(12, 12, 0, 12, 12, 12);
      gr.addColorStop(0, `rgba(${p.c},1)`); gr.addColorStop(.18, `rgba(${p.c},.9)`); gr.addColorStop(.45, `rgba(${p.c},.25)`); gr.addColorStop(1, `rgba(${p.c},0)`);
      g.fillStyle = gr; g.fillRect(0, 0, 24, 24); sprites[p.c] = c;
    }
    let last = 0;
    const tick = t => {
      if (!alive) return;
      requestAnimationFrame(tick);
      if (scrolling || t - last < 32 || document.querySelector('.cx-ldet-ov')) return;   // ~30 khung/s; đứng yên khi đang cuộn hoặc bảng chi tiết đang che nền
      const dt = last ? Math.min(t - last, 100) / 16.7 : 1; last = t;
      ctx.clearRect(0, 0, w, h);
      for (const p of ps) {
        p.y -= p.v * dt; if (p.y < -.02) { p.y = 1.02; p.x = Math.random(); }
        const s = p.r * 7.5;
        ctx.globalAlpha = .35 + .45 * Math.sin(t / 900 + p.a);
        ctx.drawImage(sprites[p.c], p.x * w + Math.sin(t / 3000 + p.a) * 12 - s / 2, p.y * h - s / 2, s, s);
      }
      ctx.globalAlpha = 1;
    };
    requestAnimationFrame(tick);
    dust = { canvas, stop() { alive = false; removeEventListener('resize', resize); } };
  }

  // pháo giấy khi màn kết quả thắng
  const fired = new WeakSet();
  function confetti(canvas) {
    if (fired.has(canvas) || reduce) return;
    fired.add(canvas);
    const ctx = canvas.getContext('2d'), w = canvas.width = innerWidth, h = canvas.height = innerHeight;
    const cols = ['#FFC94A', '#FF9F1C', '#36E3FF', '#3DFFA2', '#B04DFF', '#ffffff'];
    const ps = Array.from({ length: 180 }, (_, i) => ({ x: w / 2 + (Math.random() - .5) * 80, y: h * .42, vx: (Math.random() - .5) * 16, vy: -Math.random() * 16 - 5,
      s: Math.random() * 7 + 4, r: Math.random() * 6, vr: (Math.random() - .5) * .3, c: cols[i % cols.length] }));
    const t0 = performance.now() + 1050;   // nổ cùng lúc dấu "Thắng" đóng xuống
    const tick = now => {
      if (!canvas.isConnected) return;
      if (now < t0) { requestAnimationFrame(tick); return; }
      ctx.clearRect(0, 0, w, h);
      let live = 0;
      for (const p of ps) {
        p.vy += .32; p.vx *= .99; p.x += p.vx; p.y += p.vy; p.r += p.vr;
        if (p.y < h + 20) live++;
        ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.r); ctx.fillStyle = p.c; ctx.fillRect(-p.s / 2, -p.s / 4, p.s, p.s / 2); ctx.restore();
      }
      if (live) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }

  // hiệu ứng vào trang khi đổi trang trong khung career
  let lastPath = location.pathname;
  function enter() {
    if (location.pathname === lastPath) return;
    lastPath = location.pathname;
    const page = document.querySelector('.cx-page');
    if (!page || reduce) return;
    page.classList.remove('enter'); void page.offsetWidth; page.classList.add('enter');
    setTimeout(() => page.classList.remove('enter'), 700);
    scrollTo({ top: 0 });
  }

  // cảnh chủ tịch (bước 3): màu cà vạt/khăn lấy từ logo CLB → --ch-tie/--ch-tie2 (Chairman.razor có màu dự phòng)
  const tints = new Map();
  function logoColors(img) {
    const n = 40, c = document.createElement('canvas'); c.width = c.height = n;
    const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0, n, n);
    const d = g.getImageData(0, 0, n, n).data, bins = new Map();
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] < 200) continue;
      const k = (d[i] >> 5) << 6 | (d[i + 1] >> 5) << 3 | (d[i + 2] >> 5), e = bins.get(k) || { n: 0, r: 0, g: 0, b: 0 };
      e.n++; e.r += d[i]; e.g += d[i + 1]; e.b += d[i + 2]; bins.set(k, e);
    }
    const cols = [...bins.values()].filter(e => e.n >= 8).map(e => {
      const r = e.r / e.n, gg = e.g / e.n, b = e.b / e.n, mx = Math.max(r, gg, b), mn = Math.min(r, gg, b);
      return { r, g: gg, b, w: e.n * (.35 + (mx ? (mx - mn) / mx : 0)) };
    }).sort((a, b) => b.w - a.w);
    if (!cols.length) return null;
    const hex = c => '#' + [c.r, c.g, c.b].map(v => Math.round(v).toString(16).padStart(2, '0')).join('');
    const a = cols[0], b = cols.find(c => Math.hypot(c.r - a.r, c.g - a.g, c.b - a.b) > 90) || { r: 233, g: 237, b: 247 };
    const lum = c => (.2126 * c.r + .7152 * c.g + .0722 * c.b) / 255, con = (x, y) => (Math.max(lum(x), lum(y)) + .05) / (Math.min(lum(x), lum(y)) + .05);
    const ink = con(a, b) >= 2.5 ? hex(b) : lum(a) > .55 ? '#0B1430' : '#ffffff';   // chữ trên khăn đủ đọc
    return [hex(a), hex(b), ink];
  }
  function tint(el) {
    const src = el.dataset.logo; if (!src) return;
    if (!tints.has(src)) {
      tints.set(src, null);
      const img = new Image();
      img.onload = () => { try { tints.set(src, logoColors(img)); } catch { } scan(); };
      img.src = src; return;
    }
    const t = tints.get(src);
    if (t && el.style.getPropertyValue('--ch-tie') !== t[0]) { el.style.setProperty('--ch-tie', t[0]); el.style.setProperty('--ch-tie2', t[1]); el.style.setProperty('--ch-ink', t[2]); }
  }

  function scan() {
    const root = document.querySelector('.cx');
    if (!root) { dust?.stop(); dust = null; return; }
    const c = root.querySelector('.cx-dust'); if (c) startDust(c);
    root.querySelectorAll('[data-count]').forEach(count);
    root.querySelectorAll('.cx-result-ov.win .cx-confetti, .cx-signed-ov .cx-confetti').forEach(confetti);
    root.querySelectorAll('.cx-board[data-logo]').forEach(tint);
    hashScroll();
    syncSheet(root);
    enter();
  }
  // Link có #mục (vd. hlv#hop-dong từ menu): trang nạp dữ liệu xong mới có mục → cuộn tới khi mục xuất hiện, mỗi địa chỉ 1 lần.
  let hashDone = '';
  function hashScroll() {
    if (!location.hash) { hashDone = ''; return; }
    const key = location.pathname + location.hash;
    if (hashDone === key) return;
    let el = null; try { el = document.getElementById(decodeURIComponent(location.hash.slice(1))); } catch { }
    if (!el) return;
    hashDone = key;
    const top = el.getBoundingClientRect().top + scrollY - (document.querySelector('.cx-top')?.offsetHeight || 0) - (document.querySelector('.cx-sub')?.offsetHeight || 0) - 10;
    scrollTo({ top, behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
  }
  // Thanh dưới theo khu (PO 20/11, phương án A): bấm ô khu → bảng mục con trượt lên, mở ngay ở trình duyệt; Blazor chỉ nạp số liệu khu.
  // Đóng: bấm lại ô, nền tối, Esc, chọn mục. Blazor không vẽ data-open / aria-expanded nên vẽ lại không làm bảng tự đóng.
  const menuEl = () => document.getElementById('cx-menu');
  const dockBtn = k => document.querySelector(`.cx-dock [data-sheet="${k}"]`);
  function syncSheet(root) {
    const m = root.querySelector('#cx-menu'), cur = m?.getAttribute('data-open');
    if (cur && !m.querySelector('#cxs-' + cur)) { m.removeAttribute('data-open'); }
    root.querySelectorAll('.cx-dock [data-sheet]').forEach(b => b.setAttribute('aria-expanded', b.dataset.sheet === m?.getAttribute('data-open') ? 'true' : 'false'));
    root.querySelectorAll('.cx-msheet').forEach(s => s.toggleAttribute('data-open', s.id === 'cxs-' + m?.getAttribute('data-open')));
  }
  function sheet(id, refocus = true) {
    const m = menuEl(); if (!m) return;
    const cur = m.getAttribute('data-open');
    const next = id && id !== cur ? document.getElementById('cxs-' + id) : null;
    if (next) {
      m.setAttribute('data-open', id);
      syncSheet(document);
      const b = dockBtn(id)?.getBoundingClientRect(), r = next.getBoundingClientRect();
      if (b) next.style.setProperty('--x', Math.round(b.left + b.width / 2 - r.left) + 'px');
      next.querySelector('.cx-mbody')?.scrollTo(0, 0);
      setTimeout(() => next.querySelector('a[href], button:not([disabled])')?.focus({ preventScroll: true }), 60);
    } else if (cur) {
      m.removeAttribute('data-open');
      syncSheet(document);
      if (refocus) dockBtn(cur)?.focus({ preventScroll: true });
    }
  }
  document.addEventListener('click', e => {
    const t = e.target.closest?.('[data-sheet]');
    if (t) { sheet(t.dataset.sheet === 'close' ? null : t.dataset.sheet, t.dataset.sheet !== 'close'); return; }
    if (e.target.closest?.('#cx-menu a, #cx-menu form button')) sheet(null, false);
  });
  document.addEventListener('keydown', e => {
    const m = menuEl(), cur = m?.getAttribute('data-open'); if (!cur) return;
    if (e.key === 'Escape') { e.preventDefault(); sheet(null); return; }
    if (e.key !== 'Tab') return;   // giữ Tab trong bảng đang mở
    const f = [...(document.getElementById('cxs-' + cur)?.querySelectorAll('a[href], button:not([disabled])') ?? [])].filter(x => x.offsetParent !== null);
    if (!f.length) return;
    const first = f[0], last = f[f.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  });
  // Thư HLV trên điện thoại (PO 20/11): mở thư → lên đầu màn đọc; quay lại → về đúng chỗ trong danh sách. Máy tính 2 cột: không cuộn.
  let mailY = 0;
  window.cxMail = open => {
    if (!matchMedia('(max-width: 899px)').matches) return;
    if (open) { mailY = scrollY; scrollTo(0, 0); } else scrollTo(0, mailY);
  };
  let queued = false;
  new MutationObserver(() => { if (queued) return; queued = true; requestAnimationFrame(() => { queued = false; scan(); }); })
    .observe(document.documentElement, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['data-count', 'class'] });
  document.addEventListener('DOMContentLoaded', scan);
})();
