// Hồ sơ HLV career (PO 19/11): tượng bán thân 3D (three.js r128, tải lười), khung căn ảnh mặt + quét (MediaPipe Face Landmarker tải lười;
// không tải được → chiếu thẳng ảnh đã căn), ô ký hợp đồng, ảnh lưu niệm họp báo. Gọi từ Blazor qua window.Coach3d.
(() => {
  'use strict';
  const RM = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const sfx = n => { try { window.cxAudio?.fire(n); } catch { } };

  // ---------- tải lười ----------
  let threeP = null;
  const loadThree = () => threeP ??= window.THREE ? Promise.resolve(true) : new Promise(res => {
    const s = document.createElement('script'); s.src = 'lib/three/three.min.js';
    s.onload = () => res(!!window.THREE); s.onerror = () => res(false); document.head.appendChild(s);
  });
  const MP = 'https://cdn.jsdelivr.net/npm/@mediapipe/tasks-vision@0.10.14';
  let mpP = null;
  const loadLandmarker = () => mpP ??= (async () => {
    const v = await import(MP + '/vision_bundle.mjs');
    const fs = await v.FilesetResolver.forVisionTasks(MP + '/wasm');
    return v.FaceLandmarker.createFromOptions(fs, {
      baseOptions: { modelAssetPath: 'https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task' },
      runningMode: 'IMAGE', numFaces: 1,
    });
  })().catch(() => { mpP = null; return null; });
  const timeout = (p, ms) => Promise.race([p, new Promise(r => setTimeout(() => r(null), ms))]);

  const loadImg = src => new Promise(res => { const i = new Image(); i.onload = () => res(i); i.onerror = () => res(null); i.src = src; });

  // màu chính / phụ của logo CLB (áo, khăn quàng)
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

  // ---------- tượng bán thân ----------
  let B = null;
  async function bust() { if (B) return B; B = make(await loadThree()); return B; }

  function make(ok) {
    let R;
    if (ok) { try { R = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true }); } catch { ok = false; } }
    if (!ok) {
      // không có WebGL: ảnh mặt 2D tròn
      let src = '';
      return {
        ok: false, face(c) { try { src = c.toDataURL('image/jpeg', .85); } catch { } }, look() { },
        mount(el) { let i = el.querySelector('img.fb'); if (!i) { i = document.createElement('img'); i.className = 'fb'; i.alt = ''; el.appendChild(i); } if (src) i.src = src; },
        snap() { return null; },
      };
    }
    const T = THREE; R.setPixelRatio(Math.min(devicePixelRatio || 1, 2)); R.outputEncoding = T.sRGBEncoding;
    const scene = new T.Scene(), cam = new T.PerspectiveCamera(28, 1, .1, 50); cam.position.set(0, -.25, 6.8); cam.lookAt(0, -.45, 0);
    scene.add(new T.HemisphereLight(0xc4d6ff, 0x1a1030, .6));
    const L = (c, i, x, y, z) => { const l = new T.DirectionalLight(c, i); l.position.set(x, y, z); scene.add(l); };
    L(0xffffff, .85, 1.5, 2.5, 4); L(0x9fb8ff, .3, -3, 0, 3); L(0x36e3ff, 1.2, 3.6, 1.6, -3); L(0xffc94a, 1, -3.6, 1.2, -2.6);
    const root = new T.Group(); scene.add(root);
    const tex = c => { const t = new T.CanvasTexture(c); t.encoding = T.sRGBEncoding; t.anisotropy = 4; return t; };
    // đầu: hình cầu nắn cằm, mũi, hốc mắt
    const hg = new T.SphereGeometry(1, 80, 60), P = hg.attributes.position;
    for (let i = 0; i < P.count; i++) {
      let x = P.getX(i), y = P.getY(i), z = P.getZ(i);
      if (y < 0) { const k = 1 - .24 * Math.pow(-y, 1.5); x *= k; z *= 1 - .08 * (-y); }
      x *= .8; y *= 1.02; z *= .9;
      if (z > .25) {
        const f = Math.min(1, (z - .25) / .3), ax = Math.abs(x);
        z += f * (.12 * Math.exp(-x * x / .006 - (y + .2) ** 2 / .03) - .045 * Math.exp(-((ax - .27) ** 2) / .012 - ((y - .06) ** 2) / .008)
          + .025 * Math.exp(-((y - .19) ** 2) / .004 - ((ax - .25) ** 2) / .04) + .03 * Math.exp(-x * x / .03 - (y + .74) ** 2 / .02)
          + .018 * Math.exp(-((ax - .34) ** 2) / .02 - ((y + .28) ** 2) / .02));
      }
      P.setXYZ(i, x, y, z);
    }
    hg.computeVertexNormals();
    {
      const N = hg.attributes.normal, m = new Map();
      for (let i = 0; i < P.count; i++) { const k = P.getX(i).toFixed(4) + ',' + P.getY(i).toFixed(4) + ',' + P.getZ(i).toFixed(4); (m.get(k) || m.set(k, []).get(k)).push(i); }
      m.forEach(ix => { if (ix.length < 2) return; const v = new T.Vector3(); ix.forEach(i => v.add(new T.Vector3(N.getX(i), N.getY(i), N.getZ(i)))); v.normalize(); ix.forEach(i => N.setXYZ(i, v.x, v.y, v.z)); });
    }
    const faceC = document.createElement('canvas'); faceC.width = 1024; faceC.height = 512; const faceT = tex(faceC);
    const skinM = new T.MeshStandardMaterial({ color: 0xc99a78, roughness: .65 });
    root.add(new T.Mesh(hg, new T.MeshStandardMaterial({ map: faceT, roughness: .62 })));
    const ear = s => { const e = new T.Mesh(new T.SphereGeometry(.18, 18, 14), skinM); e.scale.set(.42, 1, .7); e.position.set(s * .775, -.06, -.03); root.add(e); }; ear(1); ear(-1);
    const neck = new T.Mesh(new T.CylinderGeometry(.33, .4, .9, 32), skinM); neck.position.y = -1.3; root.add(neck);
    const torsoC = document.createElement('canvas'); torsoC.width = 1024; torsoC.height = 512; const torsoT = tex(torsoC);
    const torso = new T.Mesh(new T.SphereGeometry(1, 64, 40), new T.MeshStandardMaterial({ map: torsoT, roughness: .75 })); torso.scale.set(1.62, .8, .84); torso.position.y = -2.02; root.add(torso);
    const hairM = new T.MeshStandardMaterial({ color: 0x2a1d14, roughness: .85 }), hair = new T.Group(); root.add(hair);
    const scarfC = document.createElement('canvas'); scarfC.width = 256; scarfC.height = 32; const scarfT = tex(scarfC); scarfT.wrapS = T.RepeatWrapping; scarfT.repeat.set(4, 1);
    const scarf = new T.Mesh(new T.TorusGeometry(.5, .14, 18, 56), new T.MeshStandardMaterial({ map: scarfT, roughness: .9 })); scarf.rotation.x = Math.PI / 2; scarf.scale.set(1.15, 1, .95); scarf.position.y = -1.47; root.add(scarf);
    const cap = (len, sc, rot) => { const m = new T.Mesh(new T.SphereGeometry(1, 48, 24, 0, Math.PI * 2, 0, len), hairM); m.scale.set(.8 * sc, 1.02 * sc, .9 * sc); m.rotation.x = rot; return m; };
    function buildHair(st) {
      hair.clear(); if (st === 'bald') return;
      if (st === 'buzz') { hair.add(cap(1.55, 1.012, -.6)); return; }
      hair.add(cap(1.62, 1.045, -.6));
      if (st === 'quiff') { const q = new T.Mesh(new T.SphereGeometry(1, 32, 16), hairM); q.scale.set(.5, .26, .42); q.position.set(0, .9, .3); q.rotation.x = -.5; hair.add(q); }
    }
    function drawTorso(out, cl) {
      const x = torsoC.getContext('2d'), W = 1024, F = 256, a = cl?.a || '#22306e', b = cl?.b || '#ffc94a';
      if (out === 'track') { x.fillStyle = a; x.fillRect(0, 0, W, 512); x.fillStyle = b; x.fillRect(F - 170, 0, 22, 512); x.fillRect(F + 148, 0, 22, 512); x.fillStyle = '#ddd'; x.fillRect(F - 2, 40, 4, 472); x.fillStyle = b; x.fillRect(0, 0, W, 26); }
      else if (out === 'polo') { x.fillStyle = a; x.fillRect(0, 0, W, 512); x.fillStyle = b; x.beginPath(); x.moveTo(F - 70, 0); x.lineTo(F, 70); x.lineTo(F + 70, 0); x.fill(); x.fillStyle = 'rgba(0,0,0,.25)'; x.fillRect(F - 6, 40, 12, 110); }
      else {
        x.fillStyle = '#141a33'; x.fillRect(0, 0, W, 512); x.fillStyle = '#f2f2f2'; x.beginPath(); x.moveTo(F - 62, 0); x.lineTo(F, 250); x.lineTo(F + 62, 0); x.fill();
        x.fillStyle = cl?.a || '#7a0f1f'; x.beginPath(); x.moveTo(F - 13, 8); x.lineTo(F + 13, 8); x.lineTo(F + 20, 220); x.lineTo(F, 250); x.lineTo(F - 20, 220); x.closePath(); x.fill();
        x.strokeStyle = '#0a0e20'; x.lineWidth = 10; x.beginPath(); x.moveTo(F - 64, 0); x.lineTo(F - 4, 262); x.moveTo(F + 64, 0); x.lineTo(F + 4, 262); x.stroke();
        x.fillStyle = '#1b2244'; x.beginPath(); x.moveTo(F - 64, 0); x.lineTo(F - 110, 0); x.lineTo(F - 40, 200); x.closePath(); x.fill(); x.beginPath(); x.moveTo(F + 64, 0); x.lineTo(F + 110, 0); x.lineTo(F + 40, 200); x.closePath(); x.fill();
      }
      if (cl?.img) x.drawImage(cl.img, F + 88, 150, 52, 52);
      torsoT.needsUpdate = true;
    }
    function drawScarf(cl) { const x = scarfC.getContext('2d'); for (let i = 0; i < 8; i++) { x.fillStyle = i % 2 ? cl.b : cl.a; x.fillRect(i * 32, 0, 32, 32); } scarfT.needsUpdate = true; }
    // dán ảnh mặt (400×400 đã căn: mắt ở y=153, cách nhau 84) lên nửa trước của đầu (chiếu thẳng)
    function face(src, skin) {
      const x = faceC.getContext('2d'); x.fillStyle = skin; x.fillRect(0, 0, 1024, 512);
      const sd = src.getContext('2d').getImageData(0, 0, src.width, src.height).data, W = src.width, img = x.getImageData(0, 0, 1024, 512), d = img.data;
      const sr = parseInt(skin.slice(1, 3), 16), sg = parseInt(skin.slice(3, 5), 16), sb = parseInt(skin.slice(5, 7), 16);
      for (let py = 0; py < 512; py++) {
        const th = py / 512 * Math.PI, st = Math.sin(th), y = Math.cos(th);
        for (let px = 0; px < 512; px++) {
          const ph = px / 1024 * Math.PI * 2, z = Math.sin(ph) * st; if (z <= .05) continue; const xx = -Math.cos(ph) * st;
          let sx = xx * .8; const sy = y * 1.02; if (sy < 0) sx *= 1 - .24 * Math.pow(-y, 1.5);
          const fx = sx / .75, fy = (sy + .075) / .875, dd = Math.sqrt(fx * fx + fy * fy); if (dd >= 1.08) continue;
          const al = Math.max(0, Math.min(1, (1.08 - dd) / .2)) * Math.max(0, Math.min(1, (z - .05) / .3));
          const ix = Math.round(.5 * W + fx * .3 * W), iy = Math.round(.47 * W - fy * .39 * W); if (ix < 0 || iy < 0 || ix >= W || iy >= W) continue;
          const si = (iy * W + ix) * 4, o = (py * 1024 + px) * 4;
          d[o] = sr + (sd[si] - sr) * al; d[o + 1] = sg + (sd[si + 1] - sg) * al; d[o + 2] = sb + (sd[si + 2] - sb) * al;
        }
      }
      x.putImageData(img, 0, 0); faceT.needsUpdate = true; skinM.color.set(skin);
    }
    let key = '';
    function look(o) {
      const k = JSON.stringify([o.hair, o.hc, o.out, o.club?.a, o.club?.b, !!o.club?.img, o.scarf]); if (k === key) return; key = k;
      buildHair(o.hair); hairM.color.set(o.hc || '#2a1d14'); drawTorso(o.out, o.club); scarf.visible = !!(o.scarf && o.club); if (o.club) drawScarf(o.club);
    }
    // gắn vào khung, kéo để xoay
    let host = null, drag = null, yaw = 0; const t0 = performance.now();
    const ro = new ResizeObserver(() => size());
    function size() { if (!host) return; const w = host.clientWidth, h = host.clientHeight; if (!w || !h) return; R.setSize(w, h, false); cam.aspect = w / h; cam.updateProjectionMatrix(); }
    function mount(el) { if (host) ro.unobserve(host); host = el; el.prepend(R.domElement); ro.observe(el); size(); }
    const cv = R.domElement; cv.style.touchAction = 'pan-y';
    cv.addEventListener('pointerdown', e => { drag = { x: e.clientX, y: yaw }; cv.setPointerCapture(e.pointerId); });
    cv.addEventListener('pointermove', e => { if (drag) yaw = drag.y + (e.clientX - drag.x) * .012; });
    const up = () => { drag = null; }; cv.addEventListener('pointerup', up); cv.addEventListener('pointercancel', up);
    (function loop(t) {
      requestAnimationFrame(loop); if (!host || !host.isConnected || host.offsetParent === null || document.documentElement.dataset.cxScroll) return;
      if (!drag) yaw *= .96; root.rotation.y = yaw + (RM ? 0 : Math.sin((t - t0) / 1000 * .55) * .5); R.render(scene, cam);
    })(t0);
    function snap() { const y = root.rotation.y; root.rotation.y = 0; R.render(scene, cam); const c = document.createElement('canvas'); c.width = R.domElement.width; c.height = R.domElement.height; c.getContext('2d').drawImage(R.domElement, 0, 0); root.rotation.y = y; return c; }
    return { ok: true, mount, look, face, snap };
  }

  // ảnh mặt đã lưu (URL) → canvas 400×400 cho tượng
  const faces = new Map();
  async function faceCanvas(url) {
    if (!url) return null;
    if (faces.has(url)) return faces.get(url);
    const img = await loadImg(url); if (!img) return null;
    const c = document.createElement('canvas'); c.width = c.height = 400; c.getContext('2d').drawImage(img, 0, 0, 400, 400);
    faces.set(url, c); return c;
  }
  const clubs = new Map();
  async function clubLook(logo) {
    if (!logo) return null;
    if (clubs.has(logo)) return clubs.get(logo);
    const img = await loadImg(logo); if (!img) return null;
    let a = '#22306e', b = '#ffc94a'; try { [a, b] = logoColors(img); } catch { }
    const v = { a, b, img }; clubs.set(logo, v); return v;
  }

  /** Hiện tượng trong khung el. o: { face (URL), skin, hair, hc, out, logo, scarf } */
  async function show(el, o) {
    if (!el) return;
    const b = await bust();
    const fc = await faceCanvas(o.face);
    if (fc) b.face(fc, o.skin || '#c99a78');
    else if (cur.scanned) b.face(cur.canvas, cur.skin);
    b.look({ hair: o.hair, hc: o.hc, out: o.out, club: await clubLook(o.logo), scarf: o.scarf });
    if (el.isConnected) b.mount(el);
  }

  // ---------- khung căn ảnh mặt + quét ----------
  const cur = { host: null, canvas: null, img: null, zoom: 1, ox: 0, oy: 0, scanned: false, skin: '#c99a78', mesh: null };
  function draw() {
    const x = cur.canvas.getContext('2d'); x.fillStyle = '#0d1630'; x.fillRect(0, 0, 400, 400); if (!cur.img) return;
    const s = Math.max(400 / cur.img.width, 400 / cur.img.height) * cur.zoom, w = cur.img.width * s, h = cur.img.height * s;
    x.drawImage(cur.img, 200 - w / 2 + cur.ox, 200 - h / 2 + cur.oy, w, h);
  }
  function setImg(img) {
    cur.img = img; cur.zoom = 1; cur.ox = cur.oy = 0; cur.scanned = false; cur.mesh = null;
    const z = cur.host?.querySelector('[data-zoom]'); if (z) z.value = 1;
    cur.host?.classList.remove('done'); cur.host?.querySelector('[data-dots]')?.getContext('2d').clearRect(0, 0, 400, 400);
    draw();
  }
  /** Gắn khung căn ảnh (kéo, phóng, tải ảnh). existing: URL ảnh mặt đã lưu (nạp sẵn vào khung). */
  async function crop(host, existing) {
    if (!host) return;
    const fresh = cur.host !== host;
    cur.host = host; cur.canvas = host.querySelector('[data-crop]');
    if (fresh && !host.dataset.ready) {
      host.dataset.ready = '1';
      let d = null; const box = host.querySelector('.cx-crop');
      box.addEventListener('pointerdown', e => { d = { x: e.clientX, y: e.clientY, ox: cur.ox, oy: cur.oy, k: 400 / box.clientWidth }; box.setPointerCapture(e.pointerId); });
      box.addEventListener('pointermove', e => { if (!d) return; cur.ox = d.ox + (e.clientX - d.x) * d.k; cur.oy = d.oy + (e.clientY - d.y) * d.k; draw(); });
      box.addEventListener('pointerup', () => d = null); box.addEventListener('pointercancel', () => d = null);
      host.querySelector('[data-zoom]').addEventListener('input', e => { cur.zoom = +e.target.value; draw(); });
      host.querySelector('[data-file]').addEventListener('change', async e => {
        const f = e.target.files[0]; if (!f) return;
        const img = await loadImg(URL.createObjectURL(f));
        if (!img) { window.pesUi?.toast('Không đọc được ảnh này. Thử ảnh JPG hoặc PNG khác.'); return; }
        setImg(img); window.pesUi?.toast('Đã tải ảnh. Căn mặt vào khung rồi bấm Quét khuôn mặt.');
        host.dispatchEvent(new CustomEvent('facechange'));
      });
      if (existing) { const img = await loadImg(existing); if (img) { setImg(img); cur.scanned = true; host.classList.add('done'); } }
      else draw();
    } else draw();
  }

  // điểm mốc giả (khi không tải được bộ quét): vòng elip quanh mặt, mắt, miệng
  function marks() {
    let s = 7; const r = () => (s = (s * 16807) % 2147483647) / 2147483647, P = [];
    for (let ring = 1; ring <= 9; ring++) { const n = 10 + ring * 7, k = ring / 9.4; for (let i = 0; i < n; i++) { const a = i / n * Math.PI * 2 + ring * .3; P.push([200 + Math.cos(a) * 120 * k * .95, 188 + Math.sin(a) * 156 * k * .95]); } }
    [[158, 153], [242, 153]].forEach(([ex, ey]) => { for (let i = 0; i < 40; i++) { const a = i / 40 * Math.PI * 2; P.push([ex + Math.cos(a) * (i % 2 ? 18 : 11), ey + Math.sin(a) * (i % 2 ? 9 : 5)]); } });
    for (let i = 0; i < 30; i++) { const a = i / 30 * Math.PI * 2; P.push([200 + Math.cos(a) * (i % 2 ? 36 : 22), 262 + Math.sin(a) * (i % 2 ? 11 : 6)]); }
    while (P.length < 478) P.push([200 + (r() - .5) * 40, 150 + r() * 110]);
    return P.slice(0, 478);
  }

  /** Quét: căn mắt theo vạch (nếu có bộ quét), vẽ điểm mốc, dựng mặt 3D. Trả { ok, real, points }. */
  async function scan() {
    const host = cur.host; if (!host || !cur.img) return { ok: false };
    host.classList.remove('done'); host.classList.add('scanning'); sfx('scan');
    const cnt = host.querySelector('[data-count-label]');
    if (cnt) cnt.textContent = 'Đang tải bộ quét…';
    let P = null, real = false;
    const lm = await timeout(loadLandmarker(), 25000);
    if (lm) {
      try {
        const res = lm.detect(cur.img), f = res.faceLandmarks?.[0];
        if (f?.length) {
          const W = cur.img.width, H = cur.img.height, avg = ids => ids.reduce((a, i) => [a[0] + f[i].x * W / ids.length, a[1] + f[i].y * H / ids.length], [0, 0]);
          const e1 = avg([33, 133]), e2 = avg([362, 263]), dist = Math.hypot(e2[0] - e1[0], e2[1] - e1[1]);
          if (dist > 4) {
            const base = Math.max(400 / W, 400 / H), s = 84 / dist, mid = [(e1[0] + e2[0]) / 2, (e1[1] + e2[1]) / 2];
            cur.zoom = Math.min(4, Math.max(.3, s / base));
            const ss = base * cur.zoom;
            cur.ox = W * ss / 2 - mid[0] * ss; cur.oy = 153 - 200 + H * ss / 2 - mid[1] * ss;
            const z = host.querySelector('[data-zoom]'); if (z) z.value = Math.min(3, Math.max(1, cur.zoom));
            draw();
            const x0 = 200 - W * ss / 2 + cur.ox, y0 = 200 - H * ss / 2 + cur.oy;
            P = f.map(p => [x0 + p.x * W * ss, y0 + p.y * H * ss]);
            cur.mesh = JSON.stringify(f.map(p => [+(p.x * W * ss + x0).toFixed(1), +(p.y * H * ss + y0).toFixed(1), +p.z.toFixed(3)]));
            real = true;
          }
        }
      } catch { }
    }
    if (!P) { P = marks(); cur.mesh = null; }
    const d = host.querySelector('[data-dots]').getContext('2d'); d.clearRect(0, 0, 400, 400);
    await new Promise(done => {
      let i = 0; const t0 = performance.now(), dur = RM ? 10 : 2200, n0 = P.length;
      (function step(t) {
        const n = Math.min(n0, Math.round((t - t0) / dur * n0)); d.fillStyle = '#36E3FF'; d.shadowColor = '#36E3FF'; d.shadowBlur = 4;
        for (; i < n; i++) { d.beginPath(); d.arc(P[i][0], P[i][1], 1.6, 0, 7); d.fill(); }
        if (cnt) cnt.textContent = `Điểm mốc ${n}/${n0}`;
        if (n < n0) { requestAnimationFrame(step); return; }
        done();
      })(t0);
    });
    // màu da: trung bình 2 gò má
    const g = cur.canvas.getContext('2d', { willReadFrequently: true });
    const avg = (x, y) => { const q = g.getImageData(x - 7, y - 7, 14, 14).data; let r = 0, gg = 0, b = 0; for (let i = 0; i < q.length; i += 4) { r += q[i]; gg += q[i + 1]; b += q[i + 2]; } const n = q.length / 4; return [r / n, gg / n, b / n]; };
    const a = avg(140, 222), b = avg(260, 222), hx = v => Math.round(v).toString(16).padStart(2, '0');
    cur.skin = '#' + [0, 1, 2].map(i => hx((a[i] + b[i]) / 2)).join('');
    (await bust()).face(cur.canvas, cur.skin);
    cur.scanned = true;
    host.classList.replace('scanning', 'done'); setTimeout(() => d.clearRect(0, 0, 400, 400), 900); sfx('unlock');
    return { ok: true, real, points: P.length, skin: cur.skin };
  }

  const jpeg = (c, q) => new Promise(r => c.toBlob(b => b ? b.arrayBuffer().then(x => r(new Uint8Array(x))) : r(null), 'image/jpeg', q));
  /** Ảnh mặt đã căn (JPEG 400×400) để lưu. */
  const faceBytes = () => cur.scanned && cur.canvas ? jpeg(cur.canvas, .86) : Promise.resolve(null);
  const faceMesh = () => cur.mesh;

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
  /** Ảnh 640×800: phông logo CLB, tượng HLV, dải tên. Trả JPEG. */
  async function photo(o) {
    const b = await bust(), cl = await clubLook(o.logo), c = document.createElement('canvas'); c.width = 640; c.height = 800;
    const x = c.getContext('2d');
    x.fillStyle = '#0b1335'; x.fillRect(0, 0, 640, 800);
    for (let r = 0; r < 12; r++) for (let k = 0; k < 6; k++) {
      const px = k * 120 + (r % 2 ? 60 : 0) - 30, py = r * 70 - 10;
      if (cl?.img) { x.globalAlpha = .22; x.drawImage(cl.img, px + 34, py + 12, 46, 46); x.globalAlpha = 1; }
      else { x.fillStyle = 'rgba(255,201,74,.12)'; x.fillRect(px + 40, py + 20, 30, 30); }
    }
    const v = x.createRadialGradient(320, 330, 60, 320, 380, 460); v.addColorStop(0, 'rgba(5,8,22,0)'); v.addColorStop(1, 'rgba(5,8,22,.8)'); x.fillStyle = v; x.fillRect(0, 0, 640, 800);
    const fc = await faceCanvas(o.face); if (fc) b.face(fc, o.skin || '#c99a78');
    b.look({ hair: o.hair, hc: o.hc, out: o.out, club: cl, scarf: true });
    const s = b.snap();
    if (s) { const h = 640 * s.height / s.width; x.drawImage(s, 0, 70, 640, h); }
    else if (fc) { x.save(); x.beginPath(); x.arc(320, 330, 170, 0, 7); x.clip(); x.drawImage(fc, 150, 160, 340, 340); x.restore(); }
    const gr = x.createLinearGradient(0, 600, 0, 800); gr.addColorStop(0, 'rgba(7,11,26,0)'); gr.addColorStop(.4, 'rgba(7,11,26,.92)'); x.fillStyle = gr; x.fillRect(0, 600, 640, 200);
    x.fillStyle = '#FFC94A'; x.fillRect(40, 690, 6, 74);
    x.fillStyle = '#ffffff'; x.font = 'italic 800 46px "Barlow Condensed", sans-serif'; x.fillText((o.name || '').toUpperCase(), 60, 728);
    x.fillStyle = '#9aa3c4'; x.font = '500 22px "Be Vietnam Pro", sans-serif'; x.fillText(o.caption || '', 60, 760);
    if (cl?.img) x.drawImage(cl.img, 540, 690, 64, 64);
    for (const [fx, fy] of [[90, 120], [560, 200], [120, 520]]) { const f = x.createRadialGradient(fx, fy, 0, fx, fy, 70); f.addColorStop(0, 'rgba(255,255,255,.55)'); f.addColorStop(1, 'rgba(255,255,255,0)'); x.fillStyle = f; x.fillRect(fx - 70, fy - 70, 140, 140); }
    return jpeg(c, .85);
  }

  /** Nháy flash màn họp báo. */
  function flash(el) {
    if (!el || RM) return;
    el.querySelectorAll('.fl').forEach((f, i) => setTimeout(() => { f.style.left = (10 + Math.random() * 70) + '%'; f.style.top = (8 + Math.random() * 50) + '%'; f.classList.remove('go'); void f.offsetWidth; f.classList.add('go'); }, i * 160));
  }

  window.Coach3d = { show, crop, scan, faceBytes, faceMesh, pad, inked, clearPad, photo, flash, preload: () => { loadThree(); } };
})();
