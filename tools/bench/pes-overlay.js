// Logo giải góc phải trên khung game ở trang chơi Selkies (PO duyệt 22/11, kiểu A: logo trơn, không nền). Từ 23/11 trận có bảng tỉ số riêng
// (khối dưới) thì logo nằm trong bảng, bộ chạy không đặt logo góc này.
// Bộ chạy trận (tools/match17/pesmatch17.py overlay()) ghi /bench/overlay/overlay.json = {"logo": "logo-<sha>.png"} lúc giao bóng, {} khi hết trận;
// trang đọc ở pes/overlay.json (Dockerfile: pes → /bench/overlay). Logo nằm cạnh canvas (cùng khối cha) để vẫn thấy khi toàn màn hình,
// không bắt chuột/chạm. Vị trí theo khung hình thật (1280×720): cách trên 5,3%, cách phải 1,25%, cao 10%.
// PO 24/11 "không có tiếng": Selkies tạo AudioContext lúc nối, chỉ resume() khi có gói tiếng (không trong thao tác chạm) → iPhone giữ "suspended";
// nút chơi chặn chạm nên Selkies không thấy thao tác. Ghi lại mọi AudioContext, resume() trong mọi lần chạm/bấm (pha capture, trước khi bị chặn).
// iPhone gạt im lặng tắt cả Web Audio → audioSession "playback" (Safari 16.4+) để vẫn có tiếng như video.
// PO 25/11 "mượt, không cảm thấy trễ": báo nhận từng khung hình về máy chủ (PES_ACK <số khung>) + số đo mỗi 5 giây (PES_STAT).
// Máy chủ (tools/bench/selkies-smooth.py) dùng báo nhận để biết người này trễ bao nhiêu; trễ quá → bỏ hình cũ, nhảy tới khung đầy đủ mới
// (hình luôn là hiện tại, không trễ dồn rồi tua nhanh). Số đo ghi /bench/stream-stats.log. Phải chạy TRƯỚC Selkies (module chạy sau script thường).
(() => {
  const WS = window.WebSocket;
  if (!WS || WS.pesWrapped) return;
  const hook = ws => {
    let n = 0, bytes = 0, keys = 0, last = 0, gapMax = 0, gaps = 0, rLast = 0, jank = 0, rMax = 0;
    ws.addEventListener('message', ev => {
      const d = ev.data;
      if (!(d instanceof ArrayBuffer) || d.byteLength < 10) return;
      const v = new DataView(d);
      if (v.getUint8(0) !== 4) return;          // 4 = một khung H.264 cả màn
      const now = performance.now();
      if (last) { const g = now - last; if (g > gapMax) gapMax = g; if (g > 50) gaps++; }
      last = now; n++; bytes += d.byteLength; if (v.getUint8(1) === 1) keys++;
      try { ws.send('PES_ACK ' + v.getUint16(2)); } catch { /* đang đóng */ }
    });
    const raf = t => {                         // khung vẽ của trình duyệt bị khựng (>34 ms) = máy người chơi bận
      if (rLast && !document.hidden) { const g = t - rLast; if (g > 34) jank++; if (g > rMax) rMax = g; }
      rLast = t;
      if (ws.readyState < 2) requestAnimationFrame(raf);
    };
    requestAnimationFrame(raf);
    const iv = setInterval(() => {
      if (ws.readyState > 1) { clearInterval(iv); return; }
      if (ws.readyState !== 1) return;
      const c = navigator.connection || {};
      const s = { fps: +(n / 5).toFixed(1), gap_max: Math.round(gapMax), gaps50: gaps, kbps: Math.round(bytes * 8 / 5000), keys, jank,
        raf_max: Math.round(rMax), hidden: document.hidden ? 1 : 0, net: c.effectiveType || '', dl: c.downlink || null, rtt: c.rtt ?? null,
        dev: (/iPhone|iPad|Android/.exec(navigator.userAgent) || ['pc'])[0], vw: innerWidth, vh: innerHeight };
      try { ws.send('PES_STAT ' + JSON.stringify(s)); } catch { /* đang đóng */ }
      n = bytes = keys = gapMax = gaps = jank = rMax = 0;
    }, 5000);
  };
  const Wrapped = function (url, protocols) {
    const ws = protocols === undefined ? new WS(url) : new WS(url, protocols);
    try { if (/\/websockets$/.test(new URL(url, location.href).pathname)) hook(ws); } catch { /* url lạ */ }
    return ws;
  };
  Wrapped.prototype = WS.prototype;
  Object.setPrototypeOf(Wrapped, WS);
  Wrapped.pesWrapped = true;
  window.WebSocket = Wrapped;
})();

(() => {
  const list = [];
  const AC = window.AudioContext || window.webkitAudioContext;
  if (AC) {
    const Tracked = class extends AC { constructor(...a) { super(...a); list.push(this); } };
    window.AudioContext = Tracked;
    if (window.webkitAudioContext) window.webkitAudioContext = Tracked;
  }
  try { if (navigator.audioSession) navigator.audioSession.type = 'playback'; } catch { /* trình duyệt chưa hỗ trợ */ }
  const unlock = () => {
    for (const c of list) if (c.state === 'suspended' || c.state === 'interrupted') c.resume().catch(() => {});
  };
  ['pointerdown', 'touchend', 'click', 'keydown'].forEach(t => window.addEventListener(t, unlock, { capture: true, passive: true }));
  document.addEventListener('visibilitychange', () => { if (!document.hidden) setTimeout(unlock, 300); });
  window.pesAudio = list;   // để kiểm thử
})();

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
// Giảm giật: stream chạy bitrate cố định (CBR). Mặc định 12 Mbps (máy chủ tự hạ khi mạng yếu, selkies-smooth.py); bấm để lên 18 Mbps (nét hơn, mạng phải khoẻ). Giữ 60 khung/giây: 45 lệch nhịp màn 60 Hz lại giật.
// Máy cho khoảng 8–18 (docker-compose SELKIES_VIDEO_BITRATE).
// Cả 2 người cùng xem 1 luồng mã hoá: ai bật cũng áp cho cả hai.
// Tiếng (23/11): game đã có âm thanh (Selkies gửi Opus). Nút bật/tắt tiếng chỉ đổi âm lượng ở máy người xem (postMessage setVolume 0/1), nhớ theo trình duyệt.
(() => {
  // Đo 23/11 lúc đang đá: mã hoá theo dải 53–58 khung/s, có lúc khựng 0,7 s; mã hoá cả khung đều 60 khung/s. VAAPI mất hình.
  // Selkies chỉ nhận hai cờ này trong gói SETTINGS lúc nối, đọc từ localStorage. postMessage 'settings' bỏ qua chúng.
  // Script này chạy trước module Selkies (module ở head, file này cuối body) nên khoá có mặt ngay lần tải đầu.
  const LOWLAG = { video_bitrate: 12, h264_streaming_mode: true }, NORMAL = { video_bitrate: 18, h264_streaming_mode: true };
  const KEY = 'pes_lowlag', MUTE = 'pes_mute';
  const sk = location.href.split('#')[0].replace(/[^a-zA-Z0-9.-_]/g, '_');
  const skPut = (k, v) => { try { localStorage.setItem(sk + '_' + k, String(v)); } catch { /* chế độ riêng tư */ } };
  skPut('h264_streaming_mode', true);
  skPut('use_cpu', true);
  skPut('use_paint_over_quality', false);
  skPut('framerate', 60);
  // PO 25/11 "nét hơn": máy tính (màn to, mạng nhà) mặc định 18 Mbps; điện thoại 12. Người chơi bấm nút thì nhớ lựa chọn.
  // Mạng khựng thì selkies-smooth.py tự hạ bitrate chung, nên 18 không làm giật lâu.
  const phone = /iPhone|iPad|Android/.test(navigator.userAgent);
  const get = () => { try { const v = localStorage.getItem(KEY); return v === null ? phone : v !== '0'; } catch { return phone; } };
  const put = v => { try { localStorage.setItem(KEY, v ? '1' : '0'); } catch { /* chế độ riêng tư */ } };
  const muted = () => { try { return localStorage.getItem(MUTE) === '1'; } catch { return false; } };
  const putMuted = v => { try { localStorage.setItem(MUTE, v ? '1' : '0'); } catch { /* chế độ riêng tư */ } };
  const sendVol = () => window.postMessage({ type: 'setVolume', value: muted() ? 0 : 1 }, window.location.origin);
  const send = v => {
    skPut('video_bitrate', v ? 12 : 18);
    window.postMessage({ type: 'settings', settings: v ? LOWLAG : NORMAL }, window.location.origin);
  };
  skPut('video_bitrate', get() ? 12 : 18);
  const fsOk = !!(document.fullscreenEnabled || document.webkitFullscreenEnabled);
  const isFs = () => !!(document.fullscreenElement || document.webkitFullscreenElement);

  const css = document.createElement('style');
  css.textContent = `
#pes-bar{position:absolute;z-index:5;display:flex;gap:6px;padding:4px;border-radius:12px;background:rgba(15,19,29,.72);
  border:1px solid #2c303b;backdrop-filter:blur(6px);-webkit-backdrop-filter:blur(6px);transition:opacity .4s;font-family:system-ui,sans-serif}
#pes-bar.idle{opacity:.18}
#pes-bar button[hidden]{display:none}
#pes-bar button{display:flex;align-items:center;justify-content:center;width:40px;height:34px;padding:0;border:0;border-radius:9px;background:transparent;
  color:#dee1f7;font:800 13px/1 "Barlow Condensed",system-ui,sans-serif;letter-spacing:.04em;text-transform:uppercase;cursor:pointer;touch-action:manipulation}
#pes-bar button:hover{background:#262a35}
#pes-bar button[aria-pressed=true]{color:#00ff87;background:rgba(0,255,135,.12)}
#pes-bar svg{width:20px;height:20px;fill:currentColor;flex:none}
#pes-toast{position:absolute;z-index:5;padding:8px 12px;text-align:center;border-radius:10px;background:rgba(15,19,29,.9);border:1px solid #2c303b;
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
  // Tay cầm ảo PES (24/11): đội nhà trên điện thoại vốn không có gì để bấm (nút tay cầm cảm ứng của Selkies nằm ở thanh bên,
  // SELKIES_UI_SHOW_SIDEBAR=false). Lớp nút riêng: cần analog nổi bên trái (chạm đâu cũng thành tâm cần), cụm nút bên phải ghi
  // việc trong trận (đi bóng / phòng ngự). Trượt ngón qua lại giữa các nút được (vd giữ TỐC ĐỘ rồi lăn sang SÚT).
  // Báo cho Selkies như 1 tay cầm chuẩn (navigator.getGamepads) — Selkies tự đọc và gửi về máy như tay cầm thật.
  // Phím PES 2017 mặc định (tay cầm Xbox): A chuyền/ép, B sút/xoạc, X chuyền bổng-tạt/gọi đồng đội ép, Y chọc khe/thủ môn lao ra,
  // RB chạy nhanh, LB đổi người, Start tạm dừng. Người 2 (#player2…) đã có nút nổi riêng của Selkies → bỏ qua.
  const touch = !/^#player\d/.test(location.hash) && (matchMedia('(pointer: coarse)').matches || 'ontouchstart' in window);
  let padOn = false, padBtn = null;
  const PAD = 'M21 6H3a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h18a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3zm4-3a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z';
  let setPad = () => {};
  if (touch) {
    const ID = 'PES17 Touch Pad';
    const gp = { id: ID, index: 0, connected: true, mapping: 'standard', axes: [0, 0, 0, 0], timestamp: performance.now(),
      buttons: Array.from({ length: 17 }, () => ({ pressed: false, touched: false, value: 0 })) };
    let live = false;   // đã báo "gamepadconnected" → từ đó luôn có mặt (ẩn = nhả hết nút)
    const prev = navigator.getGamepads ? navigator.getGamepads.bind(navigator) : () => [];
    navigator.getGamepads = () => {
      const a = Array.from(prev() || []).slice(0, 4);
      while (a.length < 4) a.push(null);
      if (!live) return a;
      const i = a.findIndex(g => !g || g.id === ID);
      if (i >= 0) { gp.index = i; a[i] = gp; }
      return a;
    };

    const BTNS = [   // b = số nút chuẩn W3C; vị trí tính từ góc phải/dưới (px ở màn cao 390, tự co theo màn)
      { b: 0, k: 'A', t: 'Chuyền', s: 'Ép người', c: 'a', r: 104, y: 34, d: 84 },
      { b: 1, k: 'B', t: 'Sút', s: 'Xoạc', c: 'b', r: 18, y: 104, d: 76 },
      { b: 3, k: 'Y', t: 'Chọc khe', s: 'Thủ môn ra', c: 'n', r: 112, y: 140, d: 68 },
      { b: 2, k: 'X', t: 'Tạt bổng', s: 'Gọi ép', c: 'n', r: 202, y: 94, d: 68 },
      // PO 24/11: đổi chỗ Tốc độ ↔ Đổi người (Tốc độ lên góc trên phải, Đổi người xuống cạnh nút Chuyền)
      { b: 5, k: 'RB', t: 'Tốc độ', c: 'rb', r: 18, y: 200, w: 84, h: 62 },
      { b: 4, k: 'LB', t: 'Đổi người', c: 'lb', r: 196, y: 22, w: 92, h: 46 },
    ];
    const fcss = document.createElement('link');
    fcss.rel = 'stylesheet';
    fcss.href = 'https://fonts.googleapis.com/css2?family=Barlow+Condensed:wght@800&display=swap';
    document.head.appendChild(fcss);
    const pcss = document.createElement('style');
    pcss.textContent = `
#pes-pad-root{position:absolute;inset:0;z-index:4;display:none;touch-action:none;user-select:none;-webkit-user-select:none;-webkit-touch-callout:none}
#pes-pad-root.on{display:block}
#pes-pad-root .pz{position:absolute;right:0;bottom:0;width:300px;height:270px;transform-origin:100% 100%}
#pes-pad-root .pb{position:absolute;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1px;box-sizing:border-box;
  border-radius:50%;border:2px solid rgba(222,225,247,.55);background:rgba(15,19,29,.42);color:#dee1f7;
  font:800 16px/1 "Barlow Condensed","Arial Narrow",system-ui,sans-serif;text-transform:uppercase;letter-spacing:.02em;text-align:center;
  -webkit-backdrop-filter:blur(2px);backdrop-filter:blur(2px)}
#pes-pad-root .pb small{font:600 9.5px/1.1 system-ui,sans-serif;text-transform:none;letter-spacing:0;color:#9ba7b5;max-width:90%}
#pes-pad-root .pb i{position:absolute;left:-4px;top:-4px;width:18px;height:18px;border-radius:50%;background:#262a35;border:1px solid #2c303b;
  color:#9ba7b5;font:700 9px/18px system-ui,sans-serif;font-style:normal;text-align:center}
#pes-pad-root .pb.rb i,#pes-pad-root .pb.lb i{width:auto;padding:0 4px;border-radius:9px;left:-6px;top:-8px}
#pes-pad-root .pb.a{border-color:#00ff87;font-size:19px}
#pes-pad-root .pb.b{border-color:#ff4d5e;font-size:20px}
#pes-pad-root .pb.rb,#pes-pad-root .pb.lb{border-radius:12px}
#pes-pad-root .pb.rb{border-color:#ffb95f;color:#ffb95f}
#pes-pad-root .pb.lb{border-color:#00ff87;color:#00ff87}
#pes-pad-root .pb.on{background:#dee1f7;color:#0f131d}
#pes-pad-root .pb.a.on,#pes-pad-root .pb.lb.on{background:#00ff87}
#pes-pad-root .pb.b.on{background:#ff4d5e}
#pes-pad-root .pb.rb.on{background:#ffb95f;color:#0f131d}
#pes-pad-root .pb.on small{color:#0f131d}
#pes-pad-root .stk{position:absolute;width:124px;height:124px;margin:-62px 0 0 -62px;border-radius:50%;border:2px solid rgba(222,225,247,.35);
  background:rgba(15,19,29,.3);pointer-events:none}
#pes-pad-root .stk b{position:absolute;left:50%;top:50%;width:56px;height:56px;margin:-28px 0 0 -28px;border-radius:50%;background:rgba(222,225,247,.5)}
#pes-pad-root .stk.on{border-color:rgba(0,255,135,.7)}
#pes-pad-root .stk.on b{background:#00ff87}
#pes-pad-root .stk span{position:absolute;left:0;right:0;top:calc(100% + 6px);text-align:center;color:#9ba7b5;
  font:800 12px/1 "Barlow Condensed","Arial Narrow",system-ui,sans-serif;text-transform:uppercase;letter-spacing:.06em}
#pes-pad-root .stk.on span{display:none}
#pes-pad-root .top{position:absolute;right:max(10px,env(safe-area-inset-right));top:max(10px,env(safe-area-inset-top));display:flex;gap:8px}
#pes-pad-root .pt{display:flex;align-items:center;gap:6px;height:38px;padding:0 14px;border-radius:12px;border:2px solid rgba(222,225,247,.45);
  background:rgba(15,19,29,.55);color:#dee1f7;font:800 15px/1 "Barlow Condensed","Arial Narrow",system-ui,sans-serif;text-transform:uppercase;letter-spacing:.03em;white-space:nowrap}
#pes-pad-root .pt svg{width:16px;height:16px;fill:currentColor;flex:none}
#pes-pad-root .pt.on{background:#dee1f7;color:#0f131d}`;
    document.head.appendChild(pcss);

    const root = document.createElement('div');
    root.id = 'pes-pad-root';
    root.setAttribute('aria-hidden', 'true');   // chỉ để chạm khi chơi; bàn phím/tay cầm thật dùng như cũ
    root.innerHTML = `<div class="stk"><b></b><span>Di chuyển</span></div><div class="pz">`
      + BTNS.map(x => `<div class="pb ${x.c}" data-b="${x.b}" style="right:${x.r}px;bottom:${x.y}px;width:${x.w || x.d}px;height:${x.h || x.d}px">`
        + `<i>${x.k}</i>${x.t}${x.s ? `<small>${x.s}</small>` : ''}</div>`).join('') + `</div>`
      // PO 24/11: Tạm dừng + Bỏ qua. Trong PES17 cả hai là nút Start: đang đá = tạm dừng, phim/replay/cận cảnh = bỏ qua.
      + `<div class="top"><div class="pt" data-b="9">${svg('M6 5h4v14H6zm8 0h4v14h-4z')}Tạm dừng</div>`
      + `<div class="pt" data-b="9">${svg('M5 5l8 7-8 7zm8 0l8 7-8 7z')}Bỏ qua</div></div>`;
    const zone = root.querySelector('.pz'), stk = root.querySelector('.stk'), knob = stk.querySelector('b');
    const els = [...root.querySelectorAll('[data-b]')];
    const R = 50, DEAD = 0.12;
    let stickId = null, sx = 0, sy = 0, rest = { x: 0, y: 0 };
    const ptrs = new Map();

    const rect = () => root.getBoundingClientRect();
    const fit = () => {   // co cụm nút theo chiều cao màn (thiết kế cho 390 px); cần analog về chỗ nghỉ
      const r = rect();
      if (!r.height) return;
      const k = Math.max(0.7, Math.min(1.25, r.height / 390));
      zone.style.transform = `scale(${k})`;
      rest = { x: 30 + 100 * k, y: r.height - 40 - 90 * k };
      if (stickId === null) { stk.style.left = `${rest.x}px`; stk.style.top = `${rest.y}px`; }
    };
    const setAxes = (x, y) => {
      gp.axes[0] = Math.abs(x) < DEAD ? 0 : x; gp.axes[1] = Math.abs(y) < DEAD ? 0 : y;
      knob.style.transform = `translate(${x * R}px,${y * R}px)`;
      gp.timestamp = performance.now();
    };
    const press = () => {
      const hit = new Set();
      for (const p of ptrs.values()) {
        const el = document.elementFromPoint(p.x, p.y)?.closest?.('[data-b]');
        if (el && root.contains(el)) hit.add(el);
      }
      const down = new Set([...hit].map(el => +el.dataset.b));
      let fresh = false;
      for (const el of els) el.classList.toggle('on', hit.has(el));
      for (const b of new Set(els.map(el => +el.dataset.b))) {
        const on = down.has(b);
        if (gp.buttons[b].pressed !== on) {
          gp.buttons[b] = { pressed: on, touched: on, value: on ? 1 : 0 };
          fresh = fresh || on;
        }
      }
      gp.timestamp = performance.now();
      if (fresh && navigator.vibrate) try { navigator.vibrate(8); } catch { /* iPhone không có */ }
    };
    const release = () => {
      ptrs.clear(); stickId = null; stk.classList.remove('on'); setAxes(0, 0); press(); fit();
    };
    root.addEventListener('pointerdown', e => {
      e.preventDefault(); e.stopPropagation();
      const r = rect(), x = e.clientX - r.left, y = e.clientY - r.top;
      const onBtn = e.target.closest?.('[data-b]');
      if (!onBtn && stickId === null && x < r.width * 0.5) {
        stickId = e.pointerId; sx = x; sy = y;
        stk.style.left = `${x}px`; stk.style.top = `${y}px`; stk.classList.add('on'); setAxes(0, 0);
        return;
      }
      ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY }); press();
    });
    root.addEventListener('pointermove', e => {
      e.preventDefault(); e.stopPropagation();
      if (e.pointerId === stickId) {
        const r = rect();
        let dx = (e.clientX - r.left - sx) / R, dy = (e.clientY - r.top - sy) / R;
        const m = Math.hypot(dx, dy);
        if (m > 1) { dx /= m; dy /= m; }
        setAxes(dx, dy);
      } else if (ptrs.has(e.pointerId)) { ptrs.set(e.pointerId, { x: e.clientX, y: e.clientY }); press(); }
    });
    const up = e => {
      e.stopPropagation();
      if (e.pointerId === stickId) { stickId = null; stk.classList.remove('on'); setAxes(0, 0); fit(); }
      else if (ptrs.delete(e.pointerId)) press();
    };
    ['pointerup', 'pointercancel'].forEach(t => root.addEventListener(t, up));
    // chạm không lọt xuống khung game (Selkies coi chạm là chuột) và không cuộn/phóng trang
    ['touchstart', 'touchmove', 'touchend', 'touchcancel', 'mousedown', 'mouseup', 'click', 'contextmenu'].forEach(t =>
      root.addEventListener(t, e => { e.stopPropagation(); if (e.cancelable) e.preventDefault(); }, { passive: false }));

    const mount = () => {
      const c = document.getElementById('videoCanvas');
      const host = c?.parentElement || document.body;
      if (root.parentElement !== host) host.appendChild(root);
      fit();
    };
    setPad = v => {
      padOn = v;
      if (v && !live) {
        live = true;
        const ev = new Event('gamepadconnected'); ev.gamepad = gp; window.dispatchEvent(ev);
      }
      if (!v) release();
      mount();
      root.classList.toggle('on', v);
      fit();
      if (padBtn) padBtn.setAttribute('aria-pressed', String(v));
      place();
    };
    bar.insertAdjacentHTML('beforeend', `<button type="button" id="pes-pad" title="Nút chơi trên màn hình" aria-label="Nút chơi trên màn hình" aria-pressed="false">${svg(PAD)}</button>`);
    padBtn = bar.querySelector('#pes-pad');
    padBtn.addEventListener('click', e => { e.stopPropagation(); setPad(!padOn); say(padOn ? 'Nút chơi: BẬT' : 'Nút chơi: TẮT (dùng tay cầm hoặc bàn phím)'); });
    const first = e => {   // điện thoại: lần chạm đầu bật nút chơi (chạm vào thanh nút thì để nút tự xử lý)
      if (padOn || bar.contains(e.target)) return;
      setPad(true); document.removeEventListener('touchstart', first);
    };
    document.addEventListener('touchstart', first, { passive: true });
    window.addEventListener('resize', () => { if (padOn) { release(); mount(); } });
    document.addEventListener('visibilitychange', () => { if (document.hidden) release(); });
    setInterval(() => { if (padOn) mount(); }, 1000);
  }
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
    say(v ? 'Giảm giật: BẬT — hình nhẹ hơn (12 Mbps), hợp mạng yếu' : 'Giảm giật: TẮT — hình nét nhất (18 Mbps)');
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
  // PO 25/11: Selkies khoá phím Esc khi toàn màn hình (navigator.keyboard.lock có "Escape") → bấm Esc vào game, phải giữ 2 giây mới thoát.
  // Khoá lại KHÔNG có Escape (lock() mới thay bộ cũ) để Esc thoát toàn màn hình như thường. Gọi muộn vì Selkies khoá sau sự kiện.
  const freeEsc = () => {
    if (!document.fullscreenElement || !(navigator.keyboard && navigator.keyboard.lock)) return;
    navigator.keyboard.lock(['AltLeft', 'AltRight', 'Tab', 'MetaLeft', 'MetaRight', 'ContextMenu']).catch(() => {});
  };
  document.addEventListener('fullscreenchange', () => [50, 400, 1500].forEach(t => setTimeout(freeEsc, t)));
  window.addEventListener('resize', place);
  setInterval(place, 1000);
  // Gửi mức đang chọn sau khi nối máy (mặc định máy chủ 8 Mbps); gửi 2 lần cho chắc khi mạng chậm.
  [3000, 8000].forEach(t => setTimeout(() => { send(get()); sendVol(); }, t));
  sync(); wake();
})();

// Lớp lọc cỏ xanh đậm (PO 24/11) ĐÃ BỎ (PO 25/11): nó phân loại từng điểm ảnh nên các khối lệch màu nhẹ của hình nén
// (H.264) bị khuếch đại thành mảng vàng/chữ nhật trên cỏ. Muốn đổi màu cỏ phải đổi trong game, không tô trên trình duyệt.
