// Hồ sơ HLV career: khung căn ảnh mặt 2D (kéo, phóng, tách nền bằng MediaPipe Selfie Segmenter tự phục vụ ở lib/mediapipe;
// lưu PNG trong suốt khi đã tách nền, JPEG khi giữ nền gốc; 400×400), ô ký hợp đồng, ảnh lưu niệm họp báo (ghép 2D),
// đèn flash họp báo. PO 20/11: bỏ quét/tượng 3D, chỉ dùng ảnh 2D. Gọi từ Blazor qua window.CoachFace.
(() => {
  'use strict';
  const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const loadImg = src => new Promise(res => { const i = new Image(); i.onload = () => res(i); i.onerror = () => res(null); i.src = src; });

  // màu chính / phụ của logo CLB (dải khăn trong ảnh lưu niệm)
  function logoColors(img) {
    const n = 32, c = document.createElement('canvas'); c.width = c.height = n;
    const g = c.getContext('2d', { willReadFrequently: true }); g.drawImage(img, 0, 0, n, n);
    const d = g.getImageData(0, 0, n, n).data, bins = new Map();
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] < 200) continue;
      const k = (d[i] >> 5) << 6 | (d[i + 1] >> 5) << 3 | (d[i + 2] >> 5), e = bins.get(k) || { n: 0, r: 0, g: 0, b: 0 };
      e.n++; e.r += d[i]; e.g += d[i + 1]; e.b += d[i + 2]; bins.set(k, e);
    }
    const cols = [...bins.values()].filter(e => e.n >= 6).map(e => {
      const r = e.r / e.n, gg = e.g / e.n, b = e.b / e.n, mx = Math.max(r, gg, b), mn = Math.min(r, gg, b);
      return { r, g: gg, b, w: e.n * (.35 + (mx ? (mx - mn) / mx : 0)) };
    }).sort((a, b) => b.w - a.w);
    const hex = c => '#' + [c.r, c.g, c.b].map(v => Math.round(v).toString(16).padStart(2, '0')).join('');
    if (!cols.length) return ['#22306e', '#00ff87'];
    const a = cols[0], b = cols.find(c => Math.hypot(c.r - a.r, c.g - a.g, c.b - a.b) > 90) || { r: 233, g: 237, b: 247 };
    return [hex(a), hex(b)];
  }

  const clubs = new Map();
  async function clubLook(logo) {
    if (!logo) return null;
    if (clubs.has(logo)) return clubs.get(logo);
    const img = await loadImg(logo); if (!img) return null;
    let a = '#22306e', b = '#00ff87'; try { [a, b] = logoColors(img); } catch { }
    const v = { a, b, img }; clubs.set(logo, v); return v;
  }

  // ---------- tách nền (tải lười, chỉ khi cần) ----------
  const abs = p => new URL(p, document.baseURI).href;
  let segP = null;
  const loadSeg = () => segP ??= (async () => {
    const v = await import(abs('lib/mediapipe/vision_bundle.mjs'));
    const fs = await v.FilesetResolver.forVisionTasks(abs('lib/mediapipe/wasm'));
    return v.ImageSegmenter.createFromOptions(fs, {
      baseOptions: { modelAssetPath: abs('lib/mediapipe/selfie_segmenter.tflite') },
      runningMode: 'IMAGE', outputConfidenceMasks: true, outputCategoryMask: false,
    });
  })().catch(e => { console.warn('tách nền:', e); segP = null; return null; });
  const timeout = (p, ms) => Promise.race([p, new Promise(r => setTimeout(() => r(null), ms))]);

  /** Trả canvas chỉ còn người (nền trong suốt, viền mờ nhẹ), null nếu không tách được. */
  async function cutout(img) {
    const seg = await timeout(loadSeg(), 30000); if (!seg) return null;
    const k = Math.min(1, 900 / Math.max(img.width, img.height));
    const src = document.createElement('canvas'); src.width = Math.round(img.width * k); src.height = Math.round(img.height * k);
    src.getContext('2d').drawImage(img, 0, 0, src.width, src.height);
    let res;
    try { res = seg.segment(src); } catch (e) { console.warn('tách nền:', e); return null; }
    const m = res.confidenceMasks?.[0]; if (!m) { res.close?.(); return null; }
    const f = m.getAsFloat32Array(), mw = m.width, mh = m.height, mc = document.createElement('canvas'); mc.width = mw; mc.height = mh;
    const id = new ImageData(mw, mh); let on = 0;
    for (let i = 0; i < f.length; i++) {
      const t = Math.min(1, Math.max(0, (f[i] - .25) / .5)), a = t * t * (3 - 2 * t);
      id.data[i * 4] = id.data[i * 4 + 1] = id.data[i * 4 + 2] = 255; id.data[i * 4 + 3] = a * 255; if (a > .5) on++;
    }
    res.close?.();
    if (on < f.length * .03) return null;   // gần như không thấy người
    mc.getContext('2d').putImageData(id, 0, 0);
    const x = src.getContext('2d'); x.globalCompositeOperation = 'destination-in'; x.filter = 'blur(1px)';
    x.drawImage(mc, 0, 0, src.width, src.height); x.filter = 'none'; x.globalCompositeOperation = 'source-over';
    return src;
  }

  // ---------- khung căn ảnh mặt ----------
  // orig: ảnh gốc; cut: ảnh đã tách nền; want: người chơi muốn tách nền; alpha: ảnh đang hiện có nền trong suốt
  const cur = { host: null, canvas: null, img: null, orig: null, cut: null, want: true, alpha: false, zoom: 1, ox: 0, oy: 0, dirty: false, job: 0 };
  function draw(x = cur.canvas.getContext('2d'), bg = true) {
    if (bg) { x.fillStyle = '#0d1630'; x.fillRect(0, 0, 400, 400); } else x.clearRect(0, 0, 400, 400);
    if (!cur.img) return;
    const s = Math.max(400 / cur.img.width, 400 / cur.img.height) * cur.zoom, w = cur.img.width * s, h = cur.img.height * s;
    x.drawImage(cur.img, 200 - w / 2 + cur.ox, 200 - h / 2 + cur.oy, w, h);
  }
  const state = st => { const h = cur.host; if (!h) return; h.dataset.cut = st; h.dispatchEvent(new CustomEvent('cutstate', { detail: st })); };
  function show() {
    cur.img = cur.want && cur.cut ? cur.cut : cur.orig; cur.alpha = cur.want && !!cur.cut || cur.origAlpha;
    cur.host?.classList.toggle('has', !!cur.img); if (cur.canvas) draw();
  }
  function setImg(img, alpha = false) {
    cur.orig = img; cur.cut = null; cur.origAlpha = alpha; cur.zoom = 1; cur.ox = cur.oy = 0;
    const z = cur.host?.querySelector('[data-zoom]'); if (z) z.value = 1;
    show();
  }
  const changed = () => { if (!cur.img) return; cur.dirty = true; cur.host?.dispatchEvent(new CustomEvent('facechange')); };
  /** Tách nền ảnh gốc hiện tại. Trả 'ok' | 'fail' | 'noimg' | 'off'. */
  async function applyCut() {
    if (!cur.orig) return 'noimg';
    if (!cur.want) { show(); return 'off'; }
    if (cur.cut || cur.origAlpha) { show(); return 'ok'; }
    const job = ++cur.job; state('busy'); cur.host?.classList.add('cutting');
    const c = await (cur.pending = cutout(cur.orig)); cur.pending = null;
    cur.host?.classList.remove('cutting');
    if (job !== cur.job) return 'busy';
    if (!c) { state('fail'); show(); return 'fail'; }
    cur.cut = c; show(); changed(); state('ok'); return 'ok';
  }
  /** Bật/tắt tách nền (nút ở Blazor). */
  async function cut(on) { cur.want = !!on; const r = await applyCut(); if (r === 'off') { changed(); state('off'); } return r; }
  /** Gắn khung căn ảnh (kéo, phóng, tải ảnh). existing: URL ảnh mặt đã lưu; alpha: ảnh đó đã tách nền (PNG trong suốt). */
  async function crop(host, existing, alpha) {
    if (!host) return;
    cur.host = host; cur.canvas = host.querySelector('[data-crop]');
    if (!host.dataset.ready) {
      host.dataset.ready = '1';
      let d = null; const box = host.querySelector('.cx-crop');
      box.addEventListener('pointerdown', e => { if (!cur.img) return; d = { x: e.clientX, y: e.clientY, ox: cur.ox, oy: cur.oy, k: 400 / box.clientWidth }; box.setPointerCapture(e.pointerId); });
      box.addEventListener('pointermove', e => { if (!d) return; cur.ox = d.ox + (e.clientX - d.x) * d.k; cur.oy = d.oy + (e.clientY - d.y) * d.k; draw(); });
      const up = () => { if (d) changed(); d = null; }; box.addEventListener('pointerup', up); box.addEventListener('pointercancel', up);
      host.querySelector('[data-zoom]').addEventListener('input', e => { cur.zoom = +e.target.value; draw(); changed(); });
      host.querySelector('[data-file]').addEventListener('change', async e => {
        const f = e.target.files[0]; e.target.value = ''; if (!f) return;
        const img = await loadImg(URL.createObjectURL(f));
        if (!img) { window.pesUi?.toast('Không đọc được ảnh này. Thử ảnh JPG hoặc PNG khác.'); return; }
        setImg(img); changed(); state('new');
        if (cur.want && await applyCut() === 'fail') window.pesUi?.toast('Không tách được nền ảnh này, giữ nền gốc.');
      });
      cur.img = cur.orig = cur.cut = null; cur.dirty = false; cur.origAlpha = false;
      cur.want = existing ? !!alpha : true;   // ảnh cũ chưa tách nền: để người chơi tự bật
      if (existing) { const img = await loadImg(existing); if (img) setImg(img, !!alpha); else draw(); } else draw();
      state(existing ? (alpha ? 'ok' : 'off') : 'new');
    } else draw();
  }
  /** Ảnh mặt đã căn để lưu: PNG trong suốt nếu đã tách nền, JPEG nếu giữ nền; null nếu chưa có ảnh. */
  async function faceBytes() {
    if (cur.pending) await cur.pending.catch(() => null);   // đang tách nền → chờ xong rồi mới lưu
    if (!cur.img || !cur.canvas) return null;
    let out;
    if (cur.alpha) {
      const c = document.createElement('canvas'); c.width = c.height = 400; draw(c.getContext('2d'), false);
      out = await png(c);
      if (out && out.length > 390000) { const s = document.createElement('canvas'); s.width = s.height = 320; s.getContext('2d').drawImage(c, 0, 0, 320, 320); out = await png(s); }
    } else out = await jpeg(cur.canvas, .86);
    if (out) cur.dirty = false;
    return out;
  }
  const dirty = () => cur.dirty && !!cur.img;
  function clearFace() { cur.img = cur.orig = cur.cut = null; cur.alpha = cur.origAlpha = false; cur.dirty = false; cur.host?.classList.remove('has'); if (cur.canvas) draw(); state('new'); }

  const blob = (c, t, q) => new Promise(r => c.toBlob(b => b ? b.arrayBuffer().then(x => r(new Uint8Array(x))) : r(null), t, q));
  const jpeg = (c, q) => blob(c, 'image/jpeg', q), png = c => blob(c, 'image/png');

  // ---------- ký hợp đồng ----------
  function pad(c) {
    if (!c || c.dataset.ready) return; c.dataset.ready = '1'; c.dataset.ink = '0';
    const x = c.getContext('2d'), fit = () => { const r = c.getBoundingClientRect(); c.width = r.width * 2; c.height = r.height * 2; x.lineWidth = 5; x.lineCap = 'round'; x.strokeStyle = '#00ff87'; };
    fit(); let p = null;
    c.addEventListener('pointerdown', e => { const r = c.getBoundingClientRect(); p = [(e.clientX - r.left) * 2, (e.clientY - r.top) * 2]; c.setPointerCapture(e.pointerId); });
    c.addEventListener('pointermove', e => {
      if (!p) return; const r = c.getBoundingClientRect(), q = [(e.clientX - r.left) * 2, (e.clientY - r.top) * 2];
      x.beginPath(); x.moveTo(p[0], p[1]); x.lineTo(q[0], q[1]); x.stroke(); p = q; c.dataset.ink = String(+c.dataset.ink + 1);
    });
    const up = () => { p = null; c.dispatchEvent(new CustomEvent('ink')); }; c.addEventListener('pointerup', up); c.addEventListener('pointercancel', up);
  }
  const inked = c => +(c?.dataset.ink || 0);
  function clearPad(c) { if (!c) return; c.getContext('2d').clearRect(0, 0, c.width, c.height); c.dataset.ink = '0'; }

  // ---------- ảnh lưu niệm họp báo ----------
  /** Ảnh 640×800: phông logo CLB, HLV (đã tách nền: đứng thẳng; chưa: khung vàng), khăn màu CLB, tên. o.cut = ảnh mặt đã tách nền.
      o.press = { tag, score, win } (họp báo sau trận, PO 21/11): bục có logo + micro thay khăn, nhãn trên ảnh. Trả JPEG. */
  async function photo(o) {
    const cl = await clubLook(o.logo), face = o.face ? await loadImg(o.face) : null;
    const c = document.createElement('canvas'); c.width = 640; c.height = 800;
    const x = c.getContext('2d');
    x.fillStyle = '#0b1335'; x.fillRect(0, 0, 640, 800);
    for (let r = 0; r < 12; r++) for (let k = 0; k < 6; k++) {
      const px = k * 120 + (r % 2 ? 60 : 0) - 30, py = r * 70 - 10;
      if (cl?.img) { x.globalAlpha = .22; x.drawImage(cl.img, px + 34, py + 12, 46, 46); x.globalAlpha = 1; }
      else { x.fillStyle = 'rgba(255,201,74,.12)'; x.fillRect(px + 40, py + 20, 30, 30); }
    }
    const v = x.createRadialGradient(320, 330, 60, 320, 380, 460); v.addColorStop(0, 'rgba(5,8,22,0)'); v.addColorStop(1, 'rgba(5,8,22,.8)'); x.fillStyle = v; x.fillRect(0, 0, 640, 800);
    const cols = [cl?.a || '#22306e', cl?.b || '#00ff87'];
    const scarf = (cx, cy, w) => {
      x.save(); x.translate(cx, cy); x.rotate(-.04);
      for (let i = 0; i < 9; i++) { x.fillStyle = cols[i % 2]; x.fillRect(-w / 2 + i * w / 9, -18, w / 9 + 1, 36); }
      x.strokeStyle = 'rgba(0,0,0,.25)'; x.lineWidth = 2; x.strokeRect(-w / 2, -18, w, 36); x.restore();
    };
    if (face && o.cut) {
      // đã tách nền: người đứng thẳng trên phông, có quầng sáng sau lưng
      const halo = x.createRadialGradient(320, 330, 20, 320, 330, 300); halo.addColorStop(0, 'rgba(255,201,74,.32)'); halo.addColorStop(1, 'rgba(255,201,74,0)');
      x.fillStyle = halo; x.fillRect(0, 0, 640, 700);
      x.save(); x.shadowColor = 'rgba(0,0,0,.55)'; x.shadowBlur = 30; x.shadowOffsetY = 12;
      x.drawImage(face, 60, 120, 520, 520); x.restore();
      if (!o.press) scarf(320, 600, 360);
    } else {
      // giữ nền gốc: chân dung trong khung bo tròn viền vàng
      const fx = 150, fy = 90, fw = 340, fh = 400, r = 28;
      const rr = () => { x.beginPath(); x.moveTo(fx + r, fy); x.arcTo(fx + fw, fy, fx + fw, fy + fh, r); x.arcTo(fx + fw, fy + fh, fx, fy + fh, r); x.arcTo(fx, fy + fh, fx, fy, r); x.arcTo(fx, fy, fx + fw, fy, r); x.closePath(); };
      x.save(); x.shadowColor = 'rgba(255,201,74,.45)'; x.shadowBlur = 40; rr(); x.fillStyle = '#121c45'; x.fill(); x.restore();
      x.save(); rr(); x.clip();
      if (face) { const s = Math.max(fw / face.width, fh / face.height); x.drawImage(face, fx + fw / 2 - face.width * s / 2, fy + fh / 2 - face.height * s / 2, face.width * s, face.height * s); }
      else { x.fillStyle = 'rgba(255,255,255,.16)'; x.beginPath(); x.arc(320, 230, 70, 0, 7); x.fill(); x.beginPath(); x.ellipse(320, 470, 150, 150, 0, Math.PI, 0); x.fill(); }
      x.restore();
      rr(); x.lineWidth = 6; x.strokeStyle = '#00ff87'; x.stroke();
      if (!o.press) scarf(320, fy + fh - 34, 400);
    }
    if (o.press) podium(x, cl, cols, o.press);
    const gr = x.createLinearGradient(0, 600, 0, 800); gr.addColorStop(0, 'rgba(7,11,26,0)'); gr.addColorStop(.4, 'rgba(7,11,26,.92)'); x.fillStyle = gr; x.fillRect(0, 600, 640, 200);
    x.fillStyle = '#00ff87'; x.fillRect(40, 690, 6, 74);
    x.fillStyle = '#ffffff'; x.font = 'italic 800 46px "Barlow Condensed", sans-serif'; x.fillText((o.name || '').toUpperCase(), 60, 728);
    x.fillStyle = '#9aa3c4'; x.font = '500 22px "Be Vietnam Pro", sans-serif'; x.fillText(o.caption || '', 60, 760);
    if (cl?.img) x.drawImage(cl.img, 540, 690, 64, 64);
    for (const [px, py] of [[90, 120], [560, 200], [120, 520]]) { const f = x.createRadialGradient(px, py, 0, px, py, 70); f.addColorStop(0, 'rgba(255,255,255,.55)'); f.addColorStop(1, 'rgba(255,255,255,0)'); x.fillStyle = f; x.fillRect(px - 70, py - 70, 140, 140); }
    return jpeg(c, .85);
  }

  /** Bục họp báo: mặt bục màu CLB + logo, 4 micro màu đài, nhãn họp báo + tỉ số ở góc trên. */
  function podium(x, cl, cols, p) {
    const g = x.createLinearGradient(0, 560, 0, 680); g.addColorStop(0, '#1a2347'); g.addColorStop(1, '#0a1030');
    x.fillStyle = g; x.fillRect(70, 572, 500, 108);
    x.fillStyle = cols[1]; x.fillRect(70, 568, 500, 5);
    if (cl?.img) x.drawImage(cl.img, 290, 592, 60, 60);
    for (const [mx, col] of [[200, '#e8364f'], [262, '#ffb95f'], [378, '#00ff87'], [440, '#00ff87']]) {
      x.fillStyle = '#1b1f2b'; x.beginPath(); if (x.roundRect) x.roundRect(mx - 9, 500, 18, 72, 9); else x.rect(mx - 9, 500, 18, 72); x.fill();
      x.fillStyle = col; x.fillRect(mx - 16, 528, 32, 16);
    }
    const chip = (t, px, py, fg, right) => {
      x.font = 'italic 800 26px "Barlow Condensed", sans-serif'; const w = x.measureText(t).width + 28, lx = right ? px - w : px;
      x.fillStyle = 'rgba(7,11,26,.78)'; x.fillRect(lx, py, w, 40); x.strokeStyle = fg; x.lineWidth = 2; x.strokeRect(lx, py, w, 40);
      x.fillStyle = fg; x.fillText(t, lx + 14, py + 29);
    };
    if (p.tag) chip(String(p.tag).toUpperCase(), 24, 24, '#00ff87');
    if (p.score) chip(String(p.score).toUpperCase(), 616, 24, p.win > 0 ? '#00ff87' : p.win < 0 ? '#ff4d5e' : '#00ff87', true);
  }

  /** Tô màu theo logo CLB (PO 21/11, đồ hoạ trận ở Truyền thông): [data-tint-h]/[data-tint-a] = logo đội nhà/khách → --th1,--th2 / --ta1,--ta2. */
  async function tint(root) {
    for (const el of (root || document).querySelectorAll('[data-tint-h],[data-tint-a]')) {
      if (el.dataset.tinted) continue; el.dataset.tinted = '1';
      for (const [k, v] of [['h', el.dataset.tintH], ['a', el.dataset.tintA]]) {
        const cl = v ? await clubLook(v) : null; if (!cl) continue;
        el.style.setProperty(`--t${k}1`, cl.a); el.style.setProperty(`--t${k}2`, cl.b);
      }
    }
  }

  /** Nháy flash màn họp báo. */
  function flash(el) {
    if (!el || RM) return;
    el.querySelectorAll('.fl').forEach((f, i) => setTimeout(() => { f.style.left = (10 + Math.random() * 70) + '%'; f.style.top = (8 + Math.random() * 50) + '%'; f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); }, i * 160));
  }

  window.CoachFace = { crop, cut, faceBytes, dirty, clearFace, pad, inked, clearPad, photo, flash, tint };
})();
