// Hồ sơ HLV career: khung căn ảnh mặt 2D (kéo, phóng, lưu JPEG 400×400), ô ký hợp đồng, ảnh lưu niệm họp báo (ghép 2D),
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
    if (!cols.length) return ['#22306e', '#ffc94a'];
    const a = cols[0], b = cols.find(c => Math.hypot(c.r - a.r, c.g - a.g, c.b - a.b) > 90) || { r: 233, g: 237, b: 247 };
    return [hex(a), hex(b)];
  }

  const clubs = new Map();
  async function clubLook(logo) {
    if (!logo) return null;
    if (clubs.has(logo)) return clubs.get(logo);
    const img = await loadImg(logo); if (!img) return null;
    let a = '#22306e', b = '#ffc94a'; try { [a, b] = logoColors(img); } catch { }
    const v = { a, b, img }; clubs.set(logo, v); return v;
  }

  // ---------- khung căn ảnh mặt ----------
  const cur = { host: null, canvas: null, img: null, zoom: 1, ox: 0, oy: 0, dirty: false };
  function draw() {
    const x = cur.canvas.getContext('2d'); x.fillStyle = '#0d1630'; x.fillRect(0, 0, 400, 400); if (!cur.img) return;
    const s = Math.max(400 / cur.img.width, 400 / cur.img.height) * cur.zoom, w = cur.img.width * s, h = cur.img.height * s;
    x.drawImage(cur.img, 200 - w / 2 + cur.ox, 200 - h / 2 + cur.oy, w, h);
  }
  function setImg(img) {
    cur.img = img; cur.zoom = 1; cur.ox = cur.oy = 0;
    const z = cur.host?.querySelector('[data-zoom]'); if (z) z.value = 1;
    cur.host?.classList.toggle('has', !!img);
    draw();
  }
  const changed = () => { if (!cur.img) return; cur.dirty = true; cur.host?.dispatchEvent(new CustomEvent('facechange')); };
  /** Gắn khung căn ảnh (kéo, phóng, tải ảnh). existing: URL ảnh mặt đã lưu (nạp sẵn vào khung). */
  async function crop(host, existing) {
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
        setImg(img); changed();
      });
      cur.img = null; cur.dirty = false;
      if (existing) { const img = await loadImg(existing); if (img) setImg(img); else draw(); } else draw();
    } else draw();
  }
  const jpeg = (c, q) => new Promise(r => c.toBlob(b => b ? b.arrayBuffer().then(x => r(new Uint8Array(x))) : r(null), 'image/jpeg', q));
  /** Ảnh mặt đã căn (JPEG 400×400) để lưu; null nếu chưa có ảnh. */
  const faceBytes = () => cur.img && cur.canvas ? jpeg(cur.canvas, .86).then(b => { cur.dirty = false; return b; }) : Promise.resolve(null);
  const dirty = () => cur.dirty && !!cur.img;
  function clearFace() { cur.img = null; cur.dirty = false; cur.host?.classList.remove('has'); if (cur.canvas) draw(); }

  // ---------- ký hợp đồng ----------
  function pad(c) {
    if (!c || c.dataset.ready) return; c.dataset.ready = '1'; c.dataset.ink = '0';
    const x = c.getContext('2d'), fit = () => { const r = c.getBoundingClientRect(); c.width = r.width * 2; c.height = r.height * 2; x.lineWidth = 5; x.lineCap = 'round'; x.strokeStyle = '#FFC94A'; };
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
  /** Ảnh 640×800: phông logo CLB, chân dung HLV khung vàng, dải khăn màu CLB, tên. Trả JPEG. */
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
    // chân dung: khung bo tròn viền vàng
    const fx = 150, fy = 90, fw = 340, fh = 400, r = 28;
    const rr = () => { x.beginPath(); x.moveTo(fx + r, fy); x.arcTo(fx + fw, fy, fx + fw, fy + fh, r); x.arcTo(fx + fw, fy + fh, fx, fy + fh, r); x.arcTo(fx, fy + fh, fx, fy, r); x.arcTo(fx, fy, fx + fw, fy, r); x.closePath(); };
    x.save(); x.shadowColor = 'rgba(255,201,74,.45)'; x.shadowBlur = 40; rr(); x.fillStyle = '#121c45'; x.fill(); x.restore();
    x.save(); rr(); x.clip();
    if (face) { const s = Math.max(fw / face.width, fh / face.height); x.drawImage(face, fx + fw / 2 - face.width * s / 2, fy + fh / 2 - face.height * s / 2, face.width * s, face.height * s); }
    else { x.fillStyle = 'rgba(255,255,255,.16)'; x.beginPath(); x.arc(320, 230, 70, 0, 7); x.fill(); x.beginPath(); x.ellipse(320, 470, 150, 150, 0, Math.PI, 0); x.fill(); }
    x.restore();
    rr(); x.lineWidth = 6; x.strokeStyle = '#FFC94A'; x.stroke();
    // khăn CLB
    const sy = fy + fh - 34, cols = [cl?.a || '#22306e', cl?.b || '#ffc94a'];
    x.save(); x.translate(320, sy); x.rotate(-.04);
    for (let i = 0; i < 9; i++) { x.fillStyle = cols[i % 2]; x.fillRect(-200 + i * 400 / 9, -18, 400 / 9 + 1, 36); }
    x.strokeStyle = 'rgba(0,0,0,.25)'; x.lineWidth = 2; x.strokeRect(-200, -18, 400, 36); x.restore();
    const gr = x.createLinearGradient(0, 600, 0, 800); gr.addColorStop(0, 'rgba(7,11,26,0)'); gr.addColorStop(.4, 'rgba(7,11,26,.92)'); x.fillStyle = gr; x.fillRect(0, 600, 640, 200);
    x.fillStyle = '#FFC94A'; x.fillRect(40, 690, 6, 74);
    x.fillStyle = '#ffffff'; x.font = 'italic 800 46px "Barlow Condensed", sans-serif'; x.fillText((o.name || '').toUpperCase(), 60, 728);
    x.fillStyle = '#9aa3c4'; x.font = '500 22px "Be Vietnam Pro", sans-serif'; x.fillText(o.caption || '', 60, 760);
    if (cl?.img) x.drawImage(cl.img, 540, 690, 64, 64);
    for (const [px, py] of [[90, 120], [560, 200], [120, 520]]) { const f = x.createRadialGradient(px, py, 0, px, py, 70); f.addColorStop(0, 'rgba(255,255,255,.55)'); f.addColorStop(1, 'rgba(255,255,255,0)'); x.fillStyle = f; x.fillRect(px - 70, py - 70, 140, 140); }
    return jpeg(c, .85);
  }

  /** Nháy flash màn họp báo. */
  function flash(el) {
    if (!el || RM) return;
    el.querySelectorAll('.fl').forEach((f, i) => setTimeout(() => { f.style.left = (10 + Math.random() * 70) + '%'; f.style.top = (8 + Math.random() * 50) + '%'; f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); }, i * 160));
  }

  window.CoachFace = { crop, faceBytes, dirty, clearFace, pad, inked, clearPad, photo, flash };
})();
