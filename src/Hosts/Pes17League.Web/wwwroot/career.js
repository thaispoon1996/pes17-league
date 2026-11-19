// UI Career đợt 1 (Stitch "Nocturnal Apex", PO 19/11/2026): bụi sáng, đếm số, pháo giấy khi thắng, hiệu ứng vào trang. Chỉ chạy khi có .cx.
(() => {
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const money = new Intl.NumberFormat('en-US', { maximumFractionDigits: 2, useGrouping: false });   // giống Fmt.Money phía server: "4.84 tr"
  const fmt = (v, kind) => kind === 'money' ? money.format(v / 1e6) + ' tr' : String(Math.round(v));

  // đếm số: <b data-count="123" data-fmt="money">
  const counted = new WeakMap();
  function count(el) {
    const to = Number(el.dataset.count);
    if (!Number.isFinite(to) || counted.get(el) === to) return;
    const from = counted.has(el) ? counted.get(el) : (el.dataset.fmt === 'money' ? to : 0);
    counted.set(el, to);
    if (reduce || from === to) { el.textContent = fmt(to, el.dataset.fmt); return; }
    const t0 = performance.now(), dur = 900;
    const step = now => {
      const k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
      el.textContent = fmt(from + (to - from) * e, el.dataset.fmt);
      if (k < 1 && counted.get(el) === to) requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

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
    const tick = t => {
      if (!alive) return;
      ctx.clearRect(0, 0, w, h);
      for (const p of ps) {
        p.y -= p.v; if (p.y < -.02) { p.y = 1.02; p.x = Math.random(); }
        const tw = .35 + .45 * Math.sin(t / 900 + p.a);
        ctx.beginPath(); ctx.arc(p.x * w + Math.sin(t / 3000 + p.a) * 12, p.y * h, p.r, 0, 7);
        ctx.fillStyle = `rgba(${p.c},${tw})`; ctx.shadowColor = `rgba(${p.c},.8)`; ctx.shadowBlur = 6; ctx.fill();
      }
      requestAnimationFrame(tick);
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

  function scan() {
    const root = document.querySelector('.cx');
    if (!root) { dust?.stop(); dust = null; return; }
    const c = root.querySelector('.cx-dust'); if (c) startDust(c);
    root.querySelectorAll('[data-count]').forEach(count);
    root.querySelectorAll('.cx-result-ov.win .cx-confetti').forEach(confetti);
    // menu ⋯: đóng khi bấm ra ngoài / chọn mục
    enter();
  }
  document.addEventListener('click', e => {
    document.querySelectorAll('.cx-menu[open]').forEach(d => { if (!d.contains(e.target) || e.target.closest('a,button')) d.open = false; });
  });
  let queued = false;
  new MutationObserver(() => { if (queued) return; queued = true; requestAnimationFrame(() => { queued = false; scan(); }); })
    .observe(document.documentElement, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['data-count', 'class'] });
  document.addEventListener('DOMContentLoaded', scan);
})();
