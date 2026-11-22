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

// Nút Toàn màn hình + Giảm giật giữa mép trên khung game (PO 22/11; góc trên trái là bảng tỉ số, góc phải là logo giải). Nằm cùng khối cha với canvas → vẫn thấy khi toàn màn hình;
// tự mờ sau 3 giây không đụng chuột/chạm. Toàn màn hình: nhờ Selkies (postMessage requestFullscreen = phóng khối cha canvas).
// iPhone không có API toàn màn hình cho trang → báo cách xoay ngang / thêm vào màn hình chính.
// Giảm giật: stream chạy bitrate cố định (CBR) → hạ bitrate 18→8 Mbps (giữ 60 khung/giây: 45 lệch nhịp màn 60 Hz lại giật).
// Máy cho khoảng 8–18 (docker-compose SELKIES_VIDEO_BITRATE), mặc định máy chủ là 8 → trang luôn gửi mức đang chọn sau khi nối.
// Cả 2 người cùng xem 1 luồng mã hoá: ai bật cũng áp cho cả hai.
(() => {
  const LOWLAG = { video_bitrate: 8 }, NORMAL = { video_bitrate: 18 };
  const KEY = 'pes_lowlag';
  const get = () => { try { return localStorage.getItem(KEY) === '1'; } catch { return false; } };
  const put = v => { try { localStorage.setItem(KEY, v ? '1' : '0'); } catch { /* chế độ riêng tư */ } };
  const send = v => window.postMessage({ type: 'settings', settings: v ? LOWLAG : NORMAL }, window.location.origin);
  const fsOk = !!(document.fullscreenEnabled || document.webkitFullscreenEnabled);
  const isFs = () => !!(document.fullscreenElement || document.webkitFullscreenElement);

  const css = document.createElement('style');
  css.textContent = `
#pes-bar{position:absolute;z-index:3;display:flex;gap:6px;padding:4px;border-radius:12px;background:rgba(15,19,29,.72);
  border:1px solid #2c303b;backdrop-filter:blur(6px);-webkit-backdrop-filter:blur(6px);transition:opacity .4s;font-family:system-ui,sans-serif}
#pes-bar.idle{opacity:.18}
#pes-bar button{display:flex;align-items:center;justify-content:center;width:40px;height:34px;padding:0;border:0;border-radius:9px;background:transparent;
  color:#dee1f7;font:800 13px/1 "Barlow Condensed",system-ui,sans-serif;letter-spacing:.04em;text-transform:uppercase;cursor:pointer;touch-action:manipulation}
#pes-bar button:hover{background:#262a35}
#pes-bar button[aria-pressed=true]{color:#00ff87;background:rgba(0,255,135,.12)}
#pes-bar svg{width:20px;height:20px;fill:currentColor;flex:none}
#pes-toast{position:absolute;z-index:3;padding:8px 12px;text-align:center;border-radius:10px;background:rgba(15,19,29,.9);border:1px solid #2c303b;
  color:#dee1f7;font:13px/1.4 system-ui,sans-serif;max-width:280px;pointer-events:none;transition:opacity .3s;opacity:0}
@media (prefers-reduced-motion:reduce){#pes-bar,#pes-toast{transition:none}}`;
  document.head.appendChild(css);

  const svg = d => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${d}"/></svg>`;
  const FS_IN = 'M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z';
  const FS_OUT = 'M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z';
  const BOLT = 'M7 2v11h3v9l7-12h-4l4-8z';

  const bar = document.createElement('div');
  bar.id = 'pes-bar';
  bar.innerHTML = `<button type="button" id="pes-fs" title="Toàn màn hình" aria-label="Toàn màn hình">${svg(FS_IN)}</button>`
    + `<button type="button" id="pes-lag" title="Giảm giật: giảm dung lượng hình (8 Mbps) cho mạng yếu" aria-label="Giảm giật">${svg(BOLT)}</button>`;
  const toast = document.createElement('div');
  toast.id = 'pes-toast';
  toast.setAttribute('role', 'status');
  const fsBtn = bar.querySelector('#pes-fs'), lagBtn = bar.querySelector('#pes-lag');

  let tt;
  const say = msg => {
    toast.textContent = msg; toast.style.opacity = '1'; place();
    clearTimeout(tt); tt = setTimeout(() => { toast.style.opacity = '0'; }, 3500);
  };

  const place = () => {
    const c = document.getElementById('videoCanvas');
    if (!c || !c.parentElement) return;
    if (bar.parentElement !== c.parentElement) { c.parentElement.appendChild(bar); c.parentElement.appendChild(toast); }
    const bw = c.width || 1280, bh = c.height || 720, cw = c.offsetWidth, ch = c.offsetHeight;
    const k = Math.min(cw / bw, ch / bh) || 1, w = bw * k, h = bh * k;
    const x = c.offsetLeft + (cw - w) / 2, y = c.offsetTop + (ch - h) / 2;
    const cx = Math.max(x, 0) + w / 2, top = Math.max(y, 0) + 6;
    bar.style.left = `${cx - bar.offsetWidth / 2}px`; bar.style.top = `${top}px`;
    toast.style.left = `${cx - toast.offsetWidth / 2}px`; toast.style.top = `${top + 50}px`;
  };

  const sync = () => {
    const f = isFs();
    fsBtn.setAttribute('aria-pressed', String(f));
    fsBtn.innerHTML = svg(f ? FS_OUT : FS_IN);
    fsBtn.title = f ? 'Thoát toàn màn hình' : 'Toàn màn hình';
    fsBtn.setAttribute('aria-label', fsBtn.title);
    lagBtn.setAttribute('aria-pressed', String(get()));
    place();
  };

  fsBtn.addEventListener('click', e => {
    e.stopPropagation();
    if (isFs()) { (document.exitFullscreen || document.webkitExitFullscreen).call(document); return; }
    if (!fsOk) { say('iPhone không cho trang web toàn màn hình: xoay ngang máy, hoặc Chia sẻ → Thêm vào MH chính rồi mở từ biểu tượng.'); return; }
    window.postMessage({ type: 'requestFullscreen' }, window.location.origin);
  });
  lagBtn.addEventListener('click', e => {
    e.stopPropagation();
    const v = !get(); put(v); send(v); sync();
    say(v ? 'Giảm giật: BẬT — hình nhẹ hơn (8 Mbps), hợp mạng yếu' : 'Giảm giật: TẮT — hình nét nhất (18 Mbps)');
  });

  let idle;
  const wake = () => { bar.classList.remove('idle'); clearTimeout(idle); idle = setTimeout(() => bar.classList.add('idle'), 3000); };
  ['mousemove', 'touchstart', 'keydown'].forEach(ev => document.addEventListener(ev, wake, { passive: true }));
  document.addEventListener('fullscreenchange', sync);
  document.addEventListener('webkitfullscreenchange', sync);
  window.addEventListener('resize', place);
  setInterval(place, 1000);
  // Gửi mức đang chọn sau khi nối máy (mặc định máy chủ 8 Mbps); gửi 2 lần cho chắc khi mạng chậm.
  [3000, 8000].forEach(t => setTimeout(() => send(get()), t));
  sync(); wake();
})();
