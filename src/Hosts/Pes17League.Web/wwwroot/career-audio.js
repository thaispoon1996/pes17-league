// Âm thanh career (UI đợt 2, PO 19/11/2026): nhạc tự sáng tác bằng Web Audio (không dùng file, không bản quyền) + hiệu ứng.
// 3 bản nhạc theo khu: "Khán đài đêm" (trang chủ, CLB), "Ngày thi đấu" (chuẩn bị/vào trận/trận), "Phiên chợ đêm" (chuyển nhượng) — chất sân bóng: trống cổ động viên, vỗ tay, khán giả hát, còi, kèn hơi.
// Giai điệu sinh theo hạt giống cố định → mỗi bản luôn giống nhau, có đoạn A/B/A' và biến tấu. Trình duyệt chỉ cho phát sau lần chạm đầu tiên.
(() => {
  const KEY = 'cx-audio';
  const st = Object.assign({ music: true, sfx: true, mv: 0.45, sv: 0.7 }, (() => { try { return JSON.parse(localStorage.getItem(KEY)) || {}; } catch { return {}; } })());
  const save = () => { try { localStorage.setItem(KEY, JSON.stringify(st)); } catch { } };

  let ctx = null, master, musicBus, sfxBus, verb, delay, noiseBuf, duck = 1;
  const mtof = m => 440 * Math.pow(2, (m - 69) / 12);

  function init() {
    if (ctx) return true;
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return false;
    ctx = new AC();
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -16; comp.ratio.value = 4; comp.attack.value = .005; comp.release.value = .25;
    master = ctx.createGain(); master.gain.value = .9; master.connect(comp); comp.connect(ctx.destination);
    musicBus = ctx.createGain(); musicBus.connect(master);
    sfxBus = ctx.createGain(); sfxBus.connect(master);
    // vang (impulse tự tạo) + delay chấm
    verb = ctx.createConvolver();
    const len = ctx.sampleRate * 2.6, ir = ctx.createBuffer(2, len, ctx.sampleRate);
    for (let c = 0; c < 2; c++) { const d = ir.getChannelData(c); for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 2.4); }
    verb.buffer = ir;
    const vg = ctx.createGain(); vg.gain.value = .32; verb.connect(vg); vg.connect(master);
    delay = ctx.createDelay(1); const fb = ctx.createGain(), dl = ctx.createGain(), dlp = ctx.createBiquadFilter();
    fb.gain.value = .32; dl.gain.value = .22; dlp.type = 'lowpass'; dlp.frequency.value = 2400;
    delay.connect(dlp); dlp.connect(fb); fb.connect(delay); dlp.connect(dl); dl.connect(musicBus);
    noiseBuf = ctx.createBuffer(1, ctx.sampleRate * 2, ctx.sampleRate);
    const nd = noiseBuf.getChannelData(0); for (let i = 0; i < nd.length; i++) nd[i] = Math.random() * 2 - 1;
    applyVolumes();
    document.addEventListener('visibilitychange', () => { if (!ctx) return; document.hidden ? ctx.suspend() : ctx.resume(); });
    return true;
  }
  function applyVolumes() {
    if (!ctx) return;
    const t = ctx.currentTime;
    musicBus.gain.cancelScheduledValues(t); musicBus.gain.setTargetAtTime(st.music ? st.mv * .55 * duck : 0, t, .25);
    sfxBus.gain.setTargetAtTime(st.sfx ? st.sv : 0, t, .05);
  }
  function setDuck(on) { duck = on ? .28 : 1; applyVolumes(); }

  // ===== nhạc cụ =====
  function env(g, t, a, peak, d, sus, r, end) {
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(peak, t + a);
    g.gain.exponentialRampToValueAtTime(Math.max(peak * sus, .0002), t + a + d);
    g.gain.setValueAtTime(Math.max(peak * sus, .0002), end);
    g.gain.exponentialRampToValueAtTime(.0001, end + r);
  }
  function osc(type, f, t, stop, det = 0) { const o = ctx.createOscillator(); o.type = type; o.frequency.value = f; o.detune.value = det; o.start(t); o.stop(stop); return o; }
  function noise(t, stop) { const s = ctx.createBufferSource(); s.buffer = noiseBuf; s.loop = true; s.start(t, Math.random()); s.stop(stop); return s; }
  function filt(type, f, q = .7) { const b = ctx.createBiquadFilter(); b.type = type; b.frequency.value = f; b.Q.value = q; return b; }
  function send(node, out, rv = 0, dl = 0) {
    node.connect(out);
    if (rv) { const g = ctx.createGain(); g.gain.value = rv; node.connect(g); g.connect(verb); }
    if (dl) { const g = ctx.createGain(); g.gain.value = dl; node.connect(g); g.connect(delay); }
  }
  const I = {
    pad(out, t, notes, dur, v = .07) {
      const lp = filt('lowpass', 900, .5), g = ctx.createGain();
      lp.frequency.setValueAtTime(500, t); lp.frequency.linearRampToValueAtTime(1500, t + dur * .5); lp.frequency.linearRampToValueAtTime(800, t + dur);
      env(g, t, Math.min(.7, dur * .3), v, .4, .8, .9, t + dur);
      for (const m of notes) for (const d of [-8, 7]) osc('sawtooth', mtof(m), t, t + dur + 1, d).connect(lp);
      lp.connect(g); send(g, out, .5);
    },
    pluck(out, t, m, v = .09, tone = 2600) {
      const lp = filt('lowpass', tone, 1), g = ctx.createGain();
      env(g, t, .004, v, .22, .05, .15, t + .2);
      osc('triangle', mtof(m), t, t + .5).connect(lp); osc('square', mtof(m + 12), t, t + .5).connect(filt('lowpass', 1200)).connect(lp);
      lp.connect(g); send(g, out, .25, .35);
    },
    bass(out, t, m, dur, v = .16) {
      const lp = filt('lowpass', 300, 4), g = ctx.createGain();
      lp.frequency.setValueAtTime(900, t); lp.frequency.exponentialRampToValueAtTime(220, t + .18);
      env(g, t, .006, v, .12, .6, .08, t + dur);
      osc('sawtooth', mtof(m), t, t + dur + .2).connect(lp); osc('sine', mtof(m), t, t + dur + .2).connect(g);
      lp.connect(g); g.connect(out);
    },
    lead(out, t, m, dur, v = .075, bright = 1900) {
      const lp = filt('lowpass', bright, 2), g = ctx.createGain(), vib = ctx.createOscillator(), vg = ctx.createGain();
      vib.frequency.value = 5.5; vg.gain.setValueAtTime(0, t); vg.gain.linearRampToValueAtTime(9, t + Math.min(.35, dur));
      vib.connect(vg); vib.start(t); vib.stop(t + dur + .5);
      env(g, t, .02, v, .15, .7, .25, t + dur);
      for (const d of [-6, 6]) { const o = osc('square', mtof(m), t, t + dur + .5, d); vg.connect(o.detune); o.connect(lp); }
      lp.connect(g); send(g, out, .45, .4);
    },
    brass(out, t, m, dur, v = .12) {
      const lp = filt('lowpass', 600, 3), g = ctx.createGain();
      lp.frequency.setValueAtTime(500, t); lp.frequency.exponentialRampToValueAtTime(3200, t + .08); lp.frequency.exponentialRampToValueAtTime(1500, t + dur);
      env(g, t, .03, v, .2, .75, .4, t + dur);
      for (const d of [-10, 0, 10]) osc('sawtooth', mtof(m), t, t + dur + .6, d).connect(lp);
      lp.connect(g); send(g, out, .55);
    },
    bell(out, t, m, v = .08) {
      for (const [r, a] of [[1, 1], [2.76, .4], [5.4, .2]]) {
        const g = ctx.createGain(); env(g, t, .003, v * a, 1.6 / r, .01, .2, t + 1.6 / r);
        osc('sine', mtof(m) * r, t, t + 2.2).connect(g); send(g, out, .6);
      }
    },
    kick(out, t, v = .9) {
      const o = osc('sine', 150, t, t + .45), g = ctx.createGain();
      o.frequency.setValueAtTime(160, t); o.frequency.exponentialRampToValueAtTime(42, t + .13);
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + .42);
      o.connect(g); g.connect(out);
    },
    snare(out, t, v = .35) {
      const n = noise(t, t + .3), bp = filt('bandpass', 1900, .8), g = ctx.createGain();
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + .2);
      n.connect(bp); bp.connect(g); send(g, out, .3);
      const b = osc('triangle', 190, t, t + .15), bg = ctx.createGain();
      bg.gain.setValueAtTime(v * .6, t); bg.gain.exponentialRampToValueAtTime(.001, t + .1); b.connect(bg); bg.connect(out);
    },
    hat(out, t, v = .08, open = false) {
      const n = noise(t, t + .5), hp = filt('highpass', 7500), g = ctx.createGain();
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + (open ? .28 : .05));
      n.connect(hp); hp.connect(g); g.connect(out);
    },
    crash(out, t, v = .25, len = 1.8) {
      const n = noise(t, t + len), hp = filt('highpass', 4200), g = ctx.createGain();
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + len);
      n.connect(hp); hp.connect(g); send(g, out, .4);
    },
    tom(out, t, f = 180, v = .5) {
      const o = osc('sine', f, t, t + .4), g = ctx.createGain();
      o.frequency.exponentialRampToValueAtTime(f * .5, t + .25);
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + .35); o.connect(g); send(g, out, .2);
    },
    riser(out, t, dur, v = .08) {
      const n = noise(t, t + dur + .1), bp = filt('bandpass', 400, 2), g = ctx.createGain();
      bp.frequency.setValueAtTime(300, t); bp.frequency.exponentialRampToValueAtTime(7000, t + dur);
      g.gain.setValueAtTime(.0001, t); g.gain.exponentialRampToValueAtTime(v, t + dur); g.gain.exponentialRampToValueAtTime(.0001, t + dur + .08);
      n.connect(bp); bp.connect(g); send(g, out, .3);
    },
    crowd(out, t, dur, v = .18, fall = false) {
      // khán đài: nhiễu dải giữa + rung biên độ chậm (nhiều giọng)
      for (const f of [520, 900, 1500]) {
        const n = noise(t, t + dur + 1.5), bp = filt('bandpass', f, .9), g = ctx.createGain(), lfo = ctx.createOscillator(), lg = ctx.createGain();
        if (fall) bp.frequency.exponentialRampToValueAtTime(f * .45, t + dur);
        lfo.frequency.value = 2 + Math.random() * 3; lg.gain.value = v * .25; lfo.connect(lg); lg.connect(g.gain); lfo.start(t); lfo.stop(t + dur + 1.5);
        g.gain.setValueAtTime(.0001, t); g.gain.exponentialRampToValueAtTime(v, t + .35); g.gain.setValueAtTime(v, t + dur * .6); g.gain.exponentialRampToValueAtTime(.0001, t + dur + 1.2);
        n.connect(bp); bp.connect(g); send(g, out, .5);
      }
    },
    whistle(out, t, len, v = .09) {
      const o = osc('sine', 2900, t, t + len + .05), lfo = ctx.createOscillator(), lg = ctx.createGain(), g = ctx.createGain();
      lfo.frequency.value = 32; lg.gain.value = 140; lfo.connect(lg); lg.connect(o.frequency); lfo.start(t); lfo.stop(t + len + .05);
      g.gain.setValueAtTime(.0001, t); g.gain.exponentialRampToValueAtTime(v, t + .015); g.gain.setValueAtTime(v, t + len - .03); g.gain.exponentialRampToValueAtTime(.0001, t + len);
      o.connect(g); send(g, out, .25);
    },
    clap(out, t, v = .3) {   // nhiều người vỗ tay lệch nhau vài ms
      for (let k = 0; k < 7; k++) {
        const t0 = t + Math.random() * .028, n = noise(t0, t0 + .12), bp = filt('bandpass', 1000 + Math.random() * 900, 1.4), g = ctx.createGain();
        g.gain.setValueAtTime(v * .45, t0); g.gain.exponentialRampToValueAtTime(.001, t0 + .07);
        n.connect(bp); bp.connect(g); send(g, out, .35);
      }
    },
    bigdrum(out, t, v = .6) {   // trống cái cổ động viên
      const o = osc('sine', 95, t, t + .6), g = ctx.createGain();
      o.frequency.setValueAtTime(100, t); o.frequency.exponentialRampToValueAtTime(52, t + .22);
      g.gain.setValueAtTime(v, t); g.gain.exponentialRampToValueAtTime(.001, t + .55); o.connect(g); send(g, out, .4);
      const n = noise(t, t + .1), lp = filt('lowpass', 600), ng = ctx.createGain();
      ng.gain.setValueAtTime(v * .5, t); ng.gain.exponentialRampToValueAtTime(.001, t + .06); n.connect(lp); lp.connect(ng); ng.connect(out);
    },
    chant(out, t, m, dur, v = .03) {   // khán giả hát "ô": nhiều giọng lệch cao độ, lọc formant
      const g = ctx.createGain(), f1 = filt('bandpass', 480, 3), f2 = filt('bandpass', 820, 4);
      env(g, t, .06, v, .1, .85, .2, t + dur);
      for (let k = 0; k < 6; k++) { const o = osc('sawtooth', mtof(m - (k % 3 === 0 ? 12 : 0)), t + Math.random() * .035, t + dur + .5, (Math.random() * 2 - 1) * 30); o.connect(f1); o.connect(f2); }
      f1.connect(g); f2.connect(g); send(g, out, .6);
    },
    horn(out, t, dur, m = 70, v = .05) {   // kèn hơi trên khán đài
      const lp = filt('lowpass', 2600, 1), g = ctx.createGain();
      env(g, t, .02, v, .05, .9, .12, t + dur);
      for (const [mm, d] of [[m, 0], [m, 14], [m + 3, -8]]) { const o = osc('sawtooth', mtof(mm), t, t + dur + .3, d); o.frequency.setValueAtTime(mtof(mm), t + dur - .05); o.frequency.linearRampToValueAtTime(mtof(mm) * .94, t + dur + .12); o.connect(lp); }
      lp.connect(g); send(g, out, .45);
    },
    whoosh(out, t, v = .06) {
      const n = noise(t, t + .4), bp = filt('bandpass', 500, 1.4), g = ctx.createGain();
      bp.frequency.setValueAtTime(350, t); bp.frequency.exponentialRampToValueAtTime(3800, t + .22);
      g.gain.setValueAtTime(.0001, t); g.gain.exponentialRampToValueAtTime(v, t + .1); g.gain.exponentialRampToValueAtTime(.0001, t + .3);
      n.connect(bp); bp.connect(g); g.connect(out);
    },
  };

  // ===== bản nhạc (tự sáng tác, PO 19/11: sôi động, chất sân bóng): hợp âm từng ô nhịp {b: bass, v: voicing}, mẫu 16 phách,
  // trống cổ động viên, vỗ tay khán đài, kèn stab, khán giả hát (giai điệu tự viết), còi + kèn hơi lúc vào bài =====
  const C = (b, ...v) => ({ b, v });
  const F4 = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
  const TRACKS = {
    hub: {
      name: 'Khán đài đêm', bpm: 122, seed: 17, swing: 0, crowd: .035, cheer: .1,
      A: [C(38, 57, 62, 65, 69), C(34, 58, 62, 65, 70), C(41, 57, 60, 65, 69), C(36, 55, 60, 64, 67)],
      B: [C(43, 55, 58, 62, 67), C(38, 57, 62, 65, 69), C(34, 58, 62, 65, 70), C(33, 57, 61, 64, 69)],
      scale: [62, 64, 65, 67, 69, 72, 74, 76, 77, 79],
      kick: F4, clap: [0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0],
      hat: [.15, .1, .7, .1, .15, .1, .7, .1, .15, .1, .7, .1, .15, .1, .7, .3], openHat: true,
      arp: [0, 1, 2, 3, 2, 1, 2, 3, 0, 1, 2, 3, 3, 2, 1, 2],
      bass: [0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 1, 0], oct: true, stab: [0, 6], lead: 2200,
      drum: [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0], drumIn: ['B', 'A2'],
      chantIn: ['A2'],
      chant: [[[0, 69, 4], [4, 69, 2], [6, 67, 2], [8, 65, 4], [12, 67, 4], [16, 69, 6], [22, 65, 2], [24, 62, 8]],
              [[0, 65, 4], [4, 65, 2], [6, 67, 2], [8, 69, 4], [12, 70, 4], [16, 69, 4], [20, 67, 4], [24, 67, 8]]],
    },
    match: {
      name: 'Ngày thi đấu', bpm: 138, seed: 41, swing: 0, crowd: .06, cheer: .2, whistle: true, horn: true,
      A: [C(40, 59, 64, 67, 71), C(36, 60, 64, 67, 72), C(43, 59, 62, 67, 71), C(38, 57, 62, 66, 69)],
      B: [C(45, 57, 60, 64, 69), C(36, 60, 64, 67, 72), C(38, 57, 62, 66, 69), C(35, 59, 63, 66, 71)],
      scale: [64, 67, 69, 71, 74, 76, 79, 81, 83],
      kick: [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0], clap: [0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1],
      hat: [.3, .2, .6, .2, .3, .2, .6, .2, .3, .2, .6, .2, .3, .2, .6, .4], openHat: true, snare: [0, 0, 0, 0, .6, 0, 0, 0, 0, 0, 0, 0, .6, 0, 0, 0],
      arp: [0, 2, 1, 3, 0, 2, 1, 3, 0, 2, 1, 3, 3, 2, 1, 0],
      bass: [1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 1], oct: true, stab: [0, 10], lead: 2800,
      drum: [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0], drumIn: ['I', 'A', 'B', 'A2'],
      chantIn: ['A', 'A2'],
      chant: [[[0, 64, 2], [2, 64, 2], [4, 67, 4], [8, 64, 2], [10, 64, 2], [12, 67, 4], [16, 67, 2], [18, 69, 2], [20, 71, 4], [24, 67, 4], [28, 64, 4]],
              [[0, 67, 2], [2, 67, 2], [4, 71, 4], [8, 67, 2], [10, 67, 2], [12, 71, 4], [16, 69, 2], [18, 69, 2], [20, 66, 4], [24, 69, 4], [28, 62, 4]]],
    },
    market: {
      name: 'Phiên chợ đêm', bpm: 114, seed: 73, swing: .14, crowd: .02, cheer: .06,
      A: [C(41, 56, 60, 65, 68), C(37, 56, 61, 65, 68), C(44, 56, 60, 63, 68), C(39, 55, 58, 63, 67)],
      B: [C(46, 58, 61, 65, 70), C(41, 56, 60, 65, 68), C(37, 56, 61, 65, 68), C(36, 55, 60, 64, 67)],
      scale: [65, 68, 70, 72, 75, 77, 80, 82],
      kick: [1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0], clap: [0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0],
      hat: [.45, .15, .3, .15, .45, .15, .3, .2, .45, .15, .3, .15, .45, .15, .35, .2],
      arp: [0, -1, 2, -1, 1, -1, 3, -1, 2, -1, 1, -1, 3, -1, 2, -1],
      bass: [1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 1, 0], stab: [0, 6, 12], lead: 1900,
      drum: null, drumIn: [], chantIn: [],
    },
  };
  // giai điệu đàn: mỗi cụm 2 ô nhịp, nhịp điệu + nốt sinh từ hạt giống (đoạn lặp lại → motif quen tai)
  function rng(seed) { return () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
  const RHYTHMS = [[0, 3, 6, 8, 10, 12, 14, 16, 22, 24, 28], [0, 4, 6, 8, 12, 16, 19, 22, 24], [0, 2, 4, 8, 11, 14, 16, 20, 24, 26, 28], [0, 3, 6, 8, 12, 14, 16, 19, 22, 24]];
  function phrase(tr, seed, chords) {
    const r = rng(seed), rh = RHYTHMS[Math.floor(r() * RHYTHMS.length)], out = [];
    let idx = Math.floor(tr.scale.length / 2);
    rh.forEach((s, k) => {
      const ch = chords[Math.floor(s / 16)];
      if (s % 8 === 0) {   // phách mạnh: nốt trong hợp âm gần nốt trước
        const tones = tr.scale.map((m, i) => [m, i]).filter(([m]) => ch.v.some(c => (c - m) % 12 === 0));
        if (tones.length) idx = tones.reduce((a, b) => Math.abs(b[1] - idx) < Math.abs(a[1] - idx) ? b : a)[1];
      } else idx = Math.max(0, Math.min(tr.scale.length - 1, idx + [-2, -1, -1, 1, 1, 2][Math.floor(r() * 6)]));
      const next = rh[k + 1] ?? 32;
      out.push({ s, m: tr.scale[idx], len: Math.min(next - s, k === rh.length - 1 ? 8 : 6) });
    });
    return out;
  }
  // bố cục 32 ô nhịp: I 0–7 dạo (khán đài, vỗ tay, trống vào dần), A 8–15, B 16–23, A' 24–31 (lên quãng 8, khán giả hát)
  const section = bar => bar < 8 ? 'I' : bar < 16 ? 'A' : bar < 24 ? 'B' : 'A2';
  function arrangement(tr) {
    if (tr.cache) return tr.cache;
    const mel = {};
    for (let bar = 8; bar < 32; bar += 2) {
      const sec = section(bar), prog = sec === 'B' ? tr.B : tr.A, ph = (bar % 8) / 2;
      const seed = tr.seed * 100 + (sec === 'B' ? 50 : 0) + (sec === 'A2' && ph === 3 ? 9 : ph % 2);
      mel[bar] = phrase(tr, seed, [prog[(bar % 4)], prog[(bar % 4) + 1]]).map(n => ({ ...n, m: n.m + (sec === 'A2' && ph >= 2 ? 12 : 0) }));
    }
    return tr.cache = mel;
  }

  let cur = null, curName = null, curGain = null, step = 0, nextT = 0, timer = null, bed = null;
  function playTrack(name) {
    if (!ctx || !st.music) { curName = name; return; }
    if (cur && curName === name) return;
    const t = ctx.currentTime;
    if (curGain) { const g = curGain; g.gain.cancelScheduledValues(t); g.gain.setTargetAtTime(0, t, .5); setTimeout(() => g.disconnect(), 3000); }
    if (bed) { const b = bed; b.g.gain.setTargetAtTime(0, t, .6); setTimeout(() => { try { b.n.stop(); } catch { } }, 3000); bed = null; }
    cur = TRACKS[name]; curName = name;
    curGain = ctx.createGain(); curGain.gain.setValueAtTime(0, t); curGain.gain.linearRampToValueAtTime(1, t + 1.5); curGain.connect(musicBus);
    if (cur.crowd) {   // tiếng sân vận động nền
      const n = noise(t, t + 3600), bp = filt('bandpass', 700, .6), g = ctx.createGain();
      g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(cur.crowd, t + 2.5);
      n.connect(bp); bp.connect(g); send(g, curGain, .3); bed = { n, g };
    }
    step = 0; nextT = t + .1;
    if (!timer) timer = setInterval(tick, 25);
    label();
  }
  function stopMusic() {
    if (!ctx) return;
    if (curGain) { const g = curGain; g.gain.setTargetAtTime(0, ctx.currentTime, .4); setTimeout(() => g.disconnect(), 2500); }
    if (bed) { const b = bed; setTimeout(() => { try { b.n.stop(); } catch { } }, 2500); bed = null; }
    clearInterval(timer); timer = null; cur = null; curGain = null;
  }
  function tick() {
    if (!cur || !ctx) return;
    const sd = 60 / cur.bpm / 4;
    while (nextT < ctx.currentTime + .18) { play(step, nextT, sd); nextT += sd; step++; }
  }
  function play(s, t, sd) {
    const tr = cur, out = curGain, bar = Math.floor(s / 16) % 32, p = s % 16;
    const T = t + (tr.swing && p % 2 ? sd * tr.swing : 0);
    const sec = section(bar), ch = (sec === 'B' ? tr.B : tr.A)[bar % 4];
    const intro = sec === 'I', build = bar >= 4 && bar < 8, fill = (bar === 15 || bar === 23 || bar === 31) && p >= 12;
    if (p === 0) I.pad(out, t, ch.v, sd * 16, intro ? .035 : .04);
    if (p === 0 && bar % 8 === 0 && !intro) { I.crash(out, t, .18); I.crowd(out, t, 2.4, tr.cheer); }
    if (p === 0 && tr.horn && (bar === 8 || bar === 24)) { I.horn(out, t - sd * 4, sd * 3, 70); }
    if (p === 0 && bar === 0 && tr.whistle) { I.whistle(out, t + .05, .2, .05); I.whistle(out, t + .35, .55, .05); }
    if (p === 0 && bar === 7) I.riser(out, t, sd * 16, .07);
    // trống cổ động viên + vỗ tay khán đài
    if (tr.drum && tr.drumIn.includes(sec) && tr.drum[p] && !fill) I.bigdrum(out, T, (intro ? .45 : .6) * tr.drum[p]);
    if (tr.clap[p] && bar >= 2 && !fill) I.clap(out, T, (intro ? .22 : .32) * tr.clap[p]);
    // bộ trống
    if ((!intro || (build && (bar >= 6 || p % 8 === 0))) && tr.kick[p] && !fill) I.kick(out, T, .85 * tr.kick[p]);
    if (!intro) {
      if (tr.hat[p]) I.hat(out, T, .07 * tr.hat[p], tr.openHat && p % 4 === 2);
      if (tr.snare?.[p] && !fill) I.snare(out, T, .28 * tr.snare[p]);
    } else if (build && p % 2 === 0) I.hat(out, T, .035);
    if (fill) { I.tom(out, T, [260, 220, 180, 150][p - 12], .4); I.snare(out, T, .15 + (p - 12) * .06); }
    // đàn
    const a = tr.arp[p];
    if (a >= 0 && bar >= 2) I.pluck(out, T, ch.v[a % ch.v.length] + (sec === 'A2' ? 12 : 0), intro ? .04 : .05, intro ? 1600 : 3000);
    if (bar >= 4 && tr.bass[p]) I.bass(out, T, ch.b + (tr.oct && p % 4 === 2 ? 12 : 0), sd * 1.5);
    if (!intro && sec !== 'B' && tr.stab.includes(p)) for (const m of ch.v) I.brass(out, T, m, sd * 1.4, .03);
    // khán giả hát / giai điệu đàn
    if (tr.chant && tr.chantIn.includes(sec)) {
      const off = (bar % 2) * 16 + p;
      for (const [s0, m, l] of tr.chant[(bar >> 1) % 2]) if (s0 === off) I.chant(out, T, m, l * sd * .95, .03);
    } else if (!intro) {
      const mel = arrangement(tr), base = bar % 2 ? bar - 1 : bar;
      if (mel[base]) for (const n of mel[base]) if (n.s === (bar - base) * 16 + p) I.lead(out, T, n.m, n.len * sd * .92, sec === 'A2' ? .065 : .055, tr.lead);
    }
  }

  // ===== hiệu ứng =====
  const sfx = {
    click() { const t = ctx.currentTime, o = osc('sine', 900, t, t + .08), g = ctx.createGain(); o.frequency.exponentialRampToValueAtTime(1500, t + .04);
      g.gain.setValueAtTime(.08, t); g.gain.exponentialRampToValueAtTime(.001, t + .07); o.connect(g); g.connect(sfxBus); },
    nav() { I.whoosh(sfxBus, ctx.currentTime, .05); },
    result(kind) {
      const t = ctx.currentTime;
      setDuck(true);
      I.whistle(sfxBus, t, .22); I.whistle(sfxBus, t + .3, .22); I.whistle(sfxBus, t + .62, .7);   // còi hết giờ
      I.kick(sfxBus, t + .5, 1); I.crash(sfxBus, t + .5, .2, 1.2);                                // tỉ số đập xuống
      const v = t + 1.05;
      if (kind === 'win') {
        [60, 64, 67].forEach((m, i) => I.brass(sfxBus, v + i * .12, m, .14, .1));
        [48, 60, 64, 67, 72].forEach(m => I.brass(sfxBus, v + .38, m, 1.4, .07));
        I.crash(sfxBus, v + .38, .3, 2.4); I.crowd(sfxBus, v + .2, 2.6, .22);
        [79, 84, 88, 91].forEach((m, i) => I.bell(sfxBus, v + .5 + i * .08, m, .04));
      } else if (kind === 'loss') {
        [57, 53, 50].forEach((m, i) => I.brass(sfxBus, v + i * .28, m, .5, .07));
        I.crowd(sfxBus, v, 1.4, .12, true);
      } else {
        I.brass(sfxBus, v, 60, .35, .07); I.brass(sfxBus, v + .4, 62, .7, .07); I.crowd(sfxBus, v, 1, .08);
      }
    },
    unlock() {
      const t = ctx.currentTime;
      setDuck(true);
      for (let k = 0; k < 4; k++) I.tom(sfxBus, t + .3 + k * .05, 900 + k * 120, .08);              // khoá rung
      const n = noise(t + .6, t + 1), hp = filt('highpass', 3000), g = ctx.createGain();            // vỡ
      g.gain.setValueAtTime(.25, t + .6); g.gain.exponentialRampToValueAtTime(.001, t + .95); n.connect(hp); hp.connect(g); send(g, sfxBus, .5);
      [72, 76, 79, 83, 86, 88].forEach((m, i) => I.bell(sfxBus, t + .72 + i * .07, m, .05));
      I.pad(sfxBus, t + .7, [60, 64, 67, 71, 74], 1.6, .05);
    },
    signed() {
      const t = ctx.currentTime;
      setDuck(true);
      I.whoosh(sfxBus, t + .1, .08);
      I.kick(sfxBus, t + 1, 1); const n = noise(t + 1, t + 1.3), lp = filt('lowpass', 900), g = ctx.createGain();   // dấu đóng
      g.gain.setValueAtTime(.35, t + 1); g.gain.exponentialRampToValueAtTime(.001, t + 1.2); n.connect(lp); lp.connect(g); g.connect(sfxBus);
      [84, 88, 91, 96].forEach((m, i) => I.bell(sfxBus, t + 1.15 + i * .06, m, .045));
      [55, 60, 64, 67].forEach(m => I.brass(sfxBus, t + 1.2, m, 1, .06));
      I.crowd(sfxBus, t + 1.1, 1.8, .16);
    },
    unveil() {   // ra mắt HLV: tiếng màn trập theo nhịp flash (career.css .flashes), khán giả + kèn
      const t = ctx.currentTime;
      setDuck(true);
      I.crash(sfxBus, t, .18, 1.6);
      for (const s of [.4, .9, 1.2, 1.9, 2.3, 3.0, 3.5, 3.8, 4.5, 4.9]) {
        const n = noise(t + s, t + s + .05), hp = filt('highpass', 2500), g = ctx.createGain();
        g.gain.setValueAtTime(.2, t + s); g.gain.exponentialRampToValueAtTime(.001, t + s + .045); n.connect(hp); hp.connect(g); g.connect(sfxBus);
      }
      [55, 60, 64, 67].forEach((m, i) => I.brass(sfxBus, t + .7 + i * .1, m, .16, .07));
      [48, 55, 60, 64, 67].forEach(m => I.brass(sfxBus, t + 1.1, m, 1.6, .055));
      I.crowd(sfxBus, t + .6, 3.2, .2);
    },
    fly() { const t = ctx.currentTime; I.whoosh(sfxBus, t, .05); I.bell(sfxBus, t + .5, 84, .04); I.bell(sfxBus, t + .58, 91, .035); },
  };
  const fire = (name, ...a) => { if (!ctx || !st.sfx) return; if (ctx.state === 'suspended') ctx.resume(); try { sfx[name](...a); } catch { } };

  // ===== nối với trang =====
  function trackFor(path) {
    if (/^\/(chuan-bi|vao-tran|tran)\b/.test(path)) return 'match';
    if (/^\/(chuyen-nhuong|de-nghi)\b/.test(path)) return 'market';
    return 'hub';
  }
  function label() {
    document.querySelectorAll('[data-snd="now"]').forEach(e => { e.textContent = !st.music ? 'Nhạc đang tắt' : cur ? '♪ ' + cur.name : '♪ Chạm để phát nhạc'; });
    document.querySelectorAll('[data-snd="music"]').forEach(e => e.setAttribute('data-on', st.music ? '1' : '0'));
    document.querySelectorAll('[data-snd-vol]').forEach(e => { const k = e.dataset.sndVol; if (document.activeElement !== e) e.value = Math.round((k === 'music' ? st.mv : st.sv) * 100); });
    document.querySelectorAll('[data-snd="sfx"]').forEach(e => { e.checked = st.sfx; });
  }
  let unlocked = false;
  function wake() {
    if (unlocked || !document.querySelector('.cx:not(.cx-boot)')) return;
    if (!init()) return;
    unlocked = true;
    ctx.resume();
    if (st.music) playTrack(trackFor(location.pathname));
  }
  ['pointerdown', 'keydown', 'touchstart'].forEach(ev => document.addEventListener(ev, wake, { capture: true, passive: true }));

  document.addEventListener('click', e => {
    const b = e.target.closest?.('[data-snd]');
    if (b?.dataset.snd === 'music') {
      e.preventDefault(); wake();
      st.music = !st.music; save();
      if (st.music) { applyVolumes(); playTrack(trackFor(location.pathname)); } else { stopMusic(); applyVolumes(); }
      label(); return;
    }
    if (!document.querySelector('.cx')) return;
    if (e.target.closest?.('.cx button, .cx a, .cx-pc, .cx summary, .ui-dialog button')) fire('click');
  }, true);
  document.addEventListener('input', e => {
    const k = e.target.dataset?.sndVol;
    if (k) { st[k === 'music' ? 'mv' : 'sv'] = e.target.value / 100; save(); applyVolumes(); }
  });
  document.addEventListener('change', e => {
    if (e.target.dataset?.snd === 'sfx') { st.sfx = e.target.checked; save(); applyVolumes(); }
  });

  const seen = new WeakSet();
  let lastPath = location.pathname, overlay = false;
  function scan() {
    const root = document.querySelector('.cx:not(.cx-boot)');
    label();
    if (!root) { if (cur) stopMusic(); return; }
    if (location.pathname !== lastPath) {
      lastPath = location.pathname;
      fire('nav');
      if (unlocked && st.music) playTrack(trackFor(lastPath));
    } else if (unlocked && st.music && !cur) playTrack(trackFor(lastPath));
    const res = root.querySelector('.cx-result-ov'), un = root.querySelector('.cx-unlock-ov'), sg = root.querySelector('.cx-signed-ov');
    for (const [el, f] of [[res, () => fire('result', res.classList.contains('win') ? 'win' : res.classList.contains('loss') ? 'loss' : 'draw')], [sg ? null : un, () => fire('unlock')], [sg, () => fire('signed')]])
      if (el && !seen.has(el)) { seen.add(el); f(); }
    root.querySelectorAll('.cx-dock a.fly').forEach(a => { if (!seen.has(a)) { seen.add(a); fire('fly'); } });
    const uv = root.querySelector('.cx-board.unveil');
    if (uv && !seen.has(uv)) { seen.add(uv); fire('unveil'); }
    const any = !!(res || un || sg || uv);
    if (any !== overlay) { overlay = any; if (!any) setDuck(false); }
  }
  let q = false;
  new MutationObserver(() => { if (q) return; q = true; requestAnimationFrame(() => { q = false; scan(); }); })
    .observe(document.documentElement, { childList: true, subtree: true });
  document.addEventListener('DOMContentLoaded', scan);
  window.cxAudio = { state: st, play: playTrack, fire };
})();
