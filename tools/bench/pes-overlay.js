// Logo giải góc phải trên khung game ở trang chơi Selkies (PO duyệt 22/11, kiểu A: logo trơn, không nền). Từ 23/11 trận có bảng tỉ số riêng
// (khối dưới) thì logo nằm trong bảng, bộ chạy không đặt logo góc này.
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

// Bảng tỉ số riêng (PO 23/11): bộ chạy trận (tools/match17/tools/board17.py) ghi pes/board.png + overlay.json
// {"board": {"v": số lần vẽ, "box": [x, y, w, h] theo màn hình thật 1280×720}} khi HUD game hiện, {"board": null} khi ẩn / hết trận.
// Ảnh phủ đúng chỗ bảng tỉ số của game. Đọc 1 lần/giây khi có bảng (5 giây khi không); ảnh mới tải xong mới thay → không nháy.
(() => {
  const img = new Image();
  img.alt = '';
  img.decoding = 'async';
  img.setAttribute('aria-hidden', 'true');
  Object.assign(img.style, { position: 'absolute', zIndex: '2', pointerEvents: 'none', userSelect: 'none', display: 'none' });
  let box = null, v = -1, timer;

  const place = () => {
    const c = document.getElementById('videoCanvas');
    if (!box || !c || !c.parentElement || !img.naturalWidth) { img.style.display = 'none'; return; }
    if (img.parentElement !== c.parentElement) c.parentElement.appendChild(img);
    const bw = c.width || 1280, bh = c.height || 720, cw = c.offsetWidth, ch = c.offsetHeight;
    if (cw < 50 || ch < 50) { img.style.display = 'none'; return; }
    const k = Math.min(cw / bw, ch / bh), w = bw * k, h = bh * k;
    const x = c.offsetLeft + (cw - w) / 2, y = c.offsetTop + (ch - h) / 2, s = w / 1280;   // box theo màn 1280×720
    Object.assign(img.style, { left: `${x + box[0] * s}px`, top: `${y + box[1] * s}px`, width: `${box[2] * s}px`, height: `${box[3] * s}px`, display: 'block' });
  };

  const poll = async () => {
    try {
      const r = await fetch(`pes/overlay.json?t=${Date.now()}`, { cache: 'no-store' });
      const b = (r.ok ? await r.json() : {}).board;
      if (b && Array.isArray(b.box) && b.box.length === 4 && Number.isFinite(b.v)) {
        if (b.v !== v) {
          const next = new Image();
          next.onload = () => { img.src = next.src; box = b.box; place(); };
          next.src = `pes/board.png?v=${b.v}`;
          v = b.v;
        } else { box = b.box; }
      } else { box = null; v = -1; }
    } catch { /* chưa có file / mạng chập chờn: giữ như cũ */ }
    place();
    clearTimeout(timer);
    timer = setTimeout(poll, box ? 1000 : 5000);
  };

  img.addEventListener('load', place);
  window.addEventListener('resize', place);
  document.addEventListener('fullscreenchange', place);
  setInterval(place, 1000);
  poll();
})();

// Nút Toàn màn hình + Giảm giật + Tiếng giữa mép trên khung game (PO 22/11, Tiếng 23/11; góc trên trái là bảng tỉ số, góc phải là logo giải). Nằm cùng khối cha với canvas → vẫn thấy khi toàn màn hình;
// tự mờ sau 3 giây không đụng chuột/chạm. Toàn màn hình: nhờ Selkies (postMessage requestFullscreen = phóng khối cha canvas).
// iPhone không có API toàn màn hình cho trang → báo cách xoay ngang / thêm vào màn hình chính.
// Giảm giật: stream chạy bitrate cố định (CBR) → hạ bitrate 18→8 Mbps (giữ 60 khung/giây: 45 lệch nhịp màn 60 Hz lại giật).
// Máy cho khoảng 8–18 (docker-compose SELKIES_VIDEO_BITRATE), mặc định máy chủ là 8 → trang luôn gửi mức đang chọn sau khi nối.
// Cả 2 người cùng xem 1 luồng mã hoá: ai bật cũng áp cho cả hai.
// Tiếng (23/11): game đã có âm thanh (Selkies gửi Opus). Nút bật/tắt tiếng chỉ đổi âm lượng ở máy người xem (postMessage setVolume 0/1), nhớ theo trình duyệt.
(() => {
  // h264_streaming_mode: mã hoá cả khung thay vì theo dải — đo 23/11 lúc đang đá (Chrome, 30 s/lần): cả khung đều 60 khung/s,
  // theo dải 53–58 khung/s và có lúc khựng 0,7 s; bitrate như nhau. VAAPI (card AMD) thử thì mất hình → giữ mã hoá CPU.
  const LOWLAG = { video_bitrate: 8, h264_streaming_mode: true }, NORMAL = { video_bitrate: 18, h264_streaming_mode: true };
  const KEY = 'pes_lowlag', MUTE = 'pes_mute';
  const get = () => { try { return localStorage.getItem(KEY) === '1'; } catch { return false; } };
  const put = v => { try { localStorage.setItem(KEY, v ? '1' : '0'); } catch { /* chế độ riêng tư */ } };
  const muted = () => { try { return localStorage.getItem(MUTE) === '1'; } catch { return false; } };
  const putMuted = v => { try { localStorage.setItem(MUTE, v ? '1' : '0'); } catch { /* chế độ riêng tư */ } };
  const sendVol = () => window.postMessage({ type: 'setVolume', value: muted() ? 0 : 1 }, window.location.origin);
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
  const VOL_ON = 'M3 9v6h4l5 5V4L7 9H3zm13.5 3A4.5 4.5 0 0 0 14 7.97v8.05A4.5 4.5 0 0 0 16.5 12zM14 3.23v2.06a7 7 0 0 1 0 13.42v2.06a9 9 0 0 0 0-17.54z';
  const VOL_OFF = 'M16.5 12A4.5 4.5 0 0 0 14 7.97v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51A8.8 8.8 0 0 0 21 12a9 9 0 0 0-7-8.77v2.06A7 7 0 0 1 19 12zM4.27 3 3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06a9 9 0 0 0 3.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4 9.91 6.09 12 8.18V4z';

  const bar = document.createElement('div');
  bar.id = 'pes-bar';
  bar.innerHTML = `<button type="button" id="pes-fs" title="Toàn màn hình" aria-label="Toàn màn hình">${svg(FS_IN)}</button>`
    + `<button type="button" id="pes-lag" title="Giảm giật: giảm dung lượng hình (8 Mbps) cho mạng yếu" aria-label="Giảm giật">${svg(BOLT)}</button>`
    + `<button type="button" id="pes-snd" aria-label="Tiếng game">${svg(VOL_ON)}</button>`;
  const toast = document.createElement('div');
  toast.id = 'pes-toast';
  toast.setAttribute('role', 'status');
  const fsBtn = bar.querySelector('#pes-fs'), lagBtn = bar.querySelector('#pes-lag'), sndBtn = bar.querySelector('#pes-snd');

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
    sndBtn.setAttribute('aria-pressed', String(!muted()));
    sndBtn.innerHTML = svg(muted() ? VOL_OFF : VOL_ON);
    sndBtn.title = muted() ? 'Bật tiếng game' : 'Tắt tiếng game';
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

  sndBtn.addEventListener('click', e => {
    e.stopPropagation();
    putMuted(!muted()); sendVol(); sync();
    say(muted() ? 'Tiếng game: TẮT' : 'Tiếng game: BẬT');
  });

  let idle;
  const wake = () => { bar.classList.remove('idle'); clearTimeout(idle); idle = setTimeout(() => bar.classList.add('idle'), 3000); };
  ['mousemove', 'touchstart', 'keydown'].forEach(ev => document.addEventListener(ev, wake, { passive: true }));
  document.addEventListener('fullscreenchange', sync);
  document.addEventListener('webkitfullscreenchange', sync);
  window.addEventListener('resize', place);
  setInterval(place, 1000);
  // Gửi mức đang chọn sau khi nối máy (mặc định máy chủ 8 Mbps); gửi 2 lần cho chắc khi mạng chậm.
  [3000, 8000].forEach(t => setTimeout(() => { send(get()); sendVol(); }, t));
  sync(); wake();
})();
