// Logo giải góc phải trên khung game ở trang chơi Selkies (PO duyệt 22/11, kiểu A: logo trơn, không nền).
// Bộ chạy trận (tools/match17/pesmatch17.py overlay()) ghi /bench/overlay/overlay.json = {"logo": "logo-<sha>.png"} lúc giao bóng, {} khi hết trận;
// trang đọc ở pes/overlay.json (Dockerfile: pes → /bench/overlay). Logo nằm cạnh canvas (cùng khối cha) để vẫn thấy khi toàn màn hình,
// không bắt chuột/chạm. Vị trí theo khung hình thật (1280×720): cách trên 5,3%, cách phải 1,25%, cao 10%.
(() => {
  const img = new Image();
  img.alt = '';
  img.decoding = 'async';
  img.setAttribute('aria-hidden', 'true');
  Object.assign(img.style, {
    position: 'absolute', zIndex: '2', pointerEvents: 'none', userSelect: 'none', display: 'none', width: 'auto',
    opacity: '0.85', filter: 'drop-shadow(0 2px 6px rgba(0,0,0,.6))',
  });
  let src = '';

  const place = () => {
    const c = document.getElementById('videoCanvas');
    if (!src || !c || !c.parentElement || !img.complete || !img.naturalWidth) { img.style.display = 'none'; return; }
    if (img.parentElement !== c.parentElement) c.parentElement.appendChild(img);
    // khung hình thật trong canvas (object-fit: contain)
    const bw = c.width || 1280, bh = c.height || 720, cw = c.offsetWidth, ch = c.offsetHeight;
    if (cw < 50 || ch < 50) { img.style.display = 'none'; return; }
    const k = Math.min(cw / bw, ch / bh), w = bw * k, h = bh * k;
    const x = c.offsetLeft + (cw - w) / 2, y = c.offsetTop + (ch - h) / 2;
    const size = h * 0.10;
    img.style.height = `${size}px`;
    img.style.top = `${y + h * 0.053}px`;
    img.style.left = `${x + w - w * 0.0125 - size * img.naturalWidth / img.naturalHeight}px`;
    img.style.display = 'block';
  };

  const poll = async () => {
    try {
      const r = await fetch(`pes/overlay.json?t=${Date.now()}`, { cache: 'no-store' });
      const j = r.ok ? await r.json() : {};
      const next = typeof j.logo === 'string' && /^logo-[\w.-]+$/.test(j.logo) ? `pes/${j.logo}` : '';
      if (next !== src) { src = next; if (src) img.src = src; }
    } catch { /* chưa có file / mạng chập chờn: giữ như cũ */ }
    place();
  };

  img.addEventListener('load', place);
  window.addEventListener('resize', place);
  document.addEventListener('fullscreenchange', place);
  setInterval(place, 1000);   // selkies đổi cỡ canvas không báo sự kiện
  setInterval(poll, 5000);
  poll();
})();
