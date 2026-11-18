// Cấu hình điều khiển (PO 04/11): bàn phím + tay cầm cắm dây trên máy tính, lưu trong trình duyệt (localStorage).
// Stream (khác origin) không đọc được localStorage của web → khi mở stream gắn cấu hình vào #ctl=…; pes-touch.js bên stream
// đọc, lưu lại bên đó rồi đổi phím/nút người dùng bấm sang phím mặc định của PES6 (game không bị sửa).
(() => {
  const KEY = 'pes-controls';
  // [mã, nhãn, phím mặc định (KeyboardEvent.code), nút tay cầm mặc định (Standard Gamepad)]
  const ACTIONS = [
    ['up', 'Lên', 'ArrowUp', 'b12'], ['down', 'Xuống', 'ArrowDown', 'b13'],
    ['left', 'Trái', 'ArrowLeft', 'b14'], ['right', 'Phải', 'ArrowRight', 'b15'],
    ['pass', 'Chuyền ✕', 'KeyX', 'b0'], ['shoot', 'Sút □', 'KeyA', 'b2'],
    ['lob', 'Chuyền bổng ○', 'KeyD', 'b1'], ['through', 'Chọc khe △', 'KeyW', 'b3'],
    ['l1', 'L1 · Đổi người', 'KeyQ', 'b4'], ['r1', 'R1 · Chạy nhanh', 'KeyE', 'b5'],
    ['l2', 'L2', 'KeyZ', 'b6'], ['r2', 'R2 · Kỹ thuật', 'KeyC', 'b7'],
    ['start', 'Start · Tạm dừng', 'Space', 'b9'], ['select', 'Select', 'F1', 'b8'],
  ];
  const SHORT = { up: 'Lên', down: 'Xuống', left: 'Trái', right: 'Phải', pass: 'Chuyền', shoot: 'Sút', lob: 'Chuyền bổng',
    through: 'Chọc khe', l1: 'Đổi người', r1: 'Chạy nhanh', l2: 'L2', r2: 'Kỹ thuật', start: 'Tạm dừng', select: 'Select' };
  const PAD_NAMES =['A / ✕', 'B / ○', 'X / □', 'Y / △', 'LB / L1', 'RB / R1', 'LT / L2', 'RT / R2', 'Back / Select', 'Start',
    'L3', 'R3', 'D-pad ↑', 'D-pad ↓', 'D-pad ←', 'D-pad →', 'Home'];

  const defaults = () => ({
    keys: Object.fromEntries(ACTIONS.map(a => [a[0], a[2]])),
    pad: Object.fromEntries(ACTIONS.map(a => [a[0], a[3]])),
  });
  const load = () => {
    const d = defaults();
    try {
      const s = JSON.parse(localStorage.getItem(KEY) || '{}');
      return { keys: { ...d.keys, ...s.keys }, pad: { ...d.pad, ...s.pad } };
    } catch { return d; }
  };
  const save = c => { try { localStorage.setItem(KEY, JSON.stringify(c)); } catch { } };
  // Máy 2 người: giữ #playerN (người 2) trước cấu hình → #player3&ctl=… (pes-touch.js trả lại #player3 trước khi selkies đọc).
  const streamUrl = url => {
    const [base, hash = ''] = url.split('#');
    const p = hash.match(/^player\d/);
    return base + '#' + (p ? p[0] + '&' : '') + 'ctl=' + encodeURIComponent(JSON.stringify(load()));
  };

  const keyName = code => !code ? '—' : code.startsWith('Key') ? code.slice(3) : code.startsWith('Digit') ? code.slice(5)
    : ({ ArrowUp: '↑', ArrowDown: '↓', ArrowLeft: '←', ArrowRight: '→', Space: 'Space' })[code] ?? code;
  const padName = b => !b ? '—' : PAD_NAMES[+b.slice(1)] ?? `Nút ${b.slice(1)}`;

  // Hình tay cầm (PO 05/11): mỗi nút ghi thao tác đang gán, sáng khi bấm nút thật, bấm vào hình = gán lại thao tác đó.
  // Chú thích hai bên: [nút, ký hiệu, điểm nối trên nút x,y, chữ x,y, căn]
  const CALLOUTS = [
    ['b6', 'L2', 180, 49, 120, 49, 'end'], ['b4', 'L1', 180, 70, 120, 70, 'end'],
    ['b12', '↑', 215, 140, 120, 112, 'end'], ['b14', '←', 186, 165, 120, 148, 'end'],
    ['b15', '→', 244, 165, 120, 184, 'end'], ['b13', '↓', 215, 190, 120, 220, 'end'],
    ['b7', 'R2', 520, 49, 580, 49, 'start'], ['b5', 'R1', 520, 70, 580, 70, 'start'],
    ['b3', '△', 498, 130, 580, 112, 'start'], ['b1', '○', 526, 165, 580, 148, 'start'],
    ['b2', '□', 450, 176, 580, 184, 'start'], ['b0', '✕', 498, 200, 580, 220, 'start'],
  ];
  const padSvg = () => `
    <svg class="padmap" viewBox="-10 0 730 300" role="img" aria-label="Sơ đồ tay cầm">
      <path class="body" d="M230,80 H470 C530,80 550,120 565,180 L590,260 C598,290 560,305 535,280 L490,240 H210 L165,280 C140,305 102,290 110,260 L135,180 C150,120 170,80 230,80 Z"/>
      ${CALLOUTS.map(([b, , x1, y1, x2, y2]) => `<line class="lead" data-b="${b}" x1="${x1}" y1="${y1}" x2="${x2 + (x2 < 350 ? 6 : -6)}" y2="${y2}"/>`).join('')}
      <g class="k" data-b="b6"><rect x="180" y="38" width="90" height="20" rx="7"/><text x="225" y="49">L2</text></g>
      <g class="k" data-b="b4"><rect x="180" y="61" width="90" height="17" rx="6"/><text x="225" y="70">L1</text></g>
      <g class="k" data-b="b7"><rect x="430" y="38" width="90" height="20" rx="7"/><text x="475" y="49">R2</text></g>
      <g class="k" data-b="b5"><rect x="430" y="61" width="90" height="17" rx="6"/><text x="475" y="70">R1</text></g>
      <g class="k" data-b="b12"><rect x="204" y="132" width="22" height="24" rx="3"/><text x="215" y="144">▲</text></g>
      <g class="k" data-b="b13"><rect x="204" y="174" width="22" height="24" rx="3"/><text x="215" y="186">▼</text></g>
      <g class="k" data-b="b14"><rect x="180" y="154" width="24" height="22" rx="3"/><text x="192" y="165">◀</text></g>
      <g class="k" data-b="b15"><rect x="226" y="154" width="24" height="22" rx="3"/><text x="238" y="165">▶</text></g>
      <g class="k face" data-b="b3"><circle cx="485" cy="133" r="15"/><text x="485" y="134">△</text></g>
      <g class="k face" data-b="b1"><circle cx="515" cy="165" r="15"/><text x="515" y="166">○</text></g>
      <g class="k face" data-b="b0"><circle cx="485" cy="197" r="15"/><text x="485" y="198">✕</text></g>
      <g class="k face" data-b="b2"><circle cx="455" cy="165" r="15"/><text x="455" y="166">□</text></g>
      <g class="k" data-b="b8"><rect x="294" y="144" width="32" height="13" rx="6"/><text class="tiny" x="310" y="135">SELECT</text></g>
      <g class="k" data-b="b9"><rect x="374" y="144" width="32" height="13" rx="6"/><text class="tiny" x="390" y="135">START</text></g>
      <text class="t mid" data-b="b8" x="310" y="174"></text><text class="t mid" data-b="b9" x="390" y="174"></text>
      <circle class="stick" cx="285" cy="208" r="22"/><circle class="stick" cx="415" cy="208" r="22"/>
      <text class="note" x="285" y="262">Analog trái: di chuyển</text>
      ${CALLOUTS.map(([b, sym, , , x, y, anchor]) => `<text class="t" data-b="${b}" data-sym="${sym}" x="${x}" y="${y}" text-anchor="${anchor}"></text>`).join('')}
    </svg>`;

  // Link mở stream (data-stream) → gắn cấu hình lúc bấm.
  addEventListener('click', ev => {
    const a = ev.target.closest?.('a[data-stream]');
    if (a) a.href = streamUrl(a.href);
  }, true);

  function mount(host) {
    let cfg = load(), wait = null;   // wait: { kind: 'keys'|'pad', action, td }
    host.innerHTML = `
      <section class="panel ctl padmap-panel">
        <div class="panel-head"><h2>Sơ đồ tay cầm</h2></div>
        ${padSvg()}
        <p class="muted">Bấm vào nút trên hình để đổi nút cho thao tác đó. Bấm nút trên tay cầm thật thì nút tương ứng sáng lên.</p>
      </section>
      <div class="grid ctl">
        <section class="panel">
          <div class="panel-head"><h2>Bàn phím</h2><button class="link" data-reset="keys">Về mặc định</button></div>
          <table class="table"><thead><tr><th class="left">Thao tác</th><th>Phím</th></tr></thead><tbody data-kind="keys"></tbody></table>
        </section>
        <section class="panel">
          <div class="panel-head"><h2>Tay cầm</h2><button class="link" data-reset="pad">Về mặc định</button></div>
          <p class="pad-status muted"></p>
          <table class="table"><thead><tr><th class="left">Thao tác</th><th>Nút</th></tr></thead><tbody data-kind="pad"></tbody></table>
          <p class="muted">Cần analog trái luôn dùng để di chuyển.</p>
        </section>
      </div>`;
    const render = () => {
      for (const tb of host.querySelectorAll('tbody')) {
        const kind = tb.dataset.kind;
        tb.innerHTML = ACTIONS.map(([id, label]) =>
          `<tr data-id="${id}"><td class="left">${label}</td><td><button class="bind">${kind === 'keys' ? keyName(cfg.keys[id]) : padName(cfg.pad[id])}</button></td></tr>`).join('');
      }
      const actionOf = b => Object.keys(cfg.pad).find(a => cfg.pad[a] === b);
      for (const t of host.querySelectorAll('.padmap text.t')) {
        const a = actionOf(t.dataset.b);
        const s = a ? SHORT[a] : '—', sym = t.dataset.sym;
        t.textContent = !sym || sym === s ? s : `${sym} ${s}`;
      }
      for (const el of host.querySelectorAll('.padmap [data-b]')) el.classList.remove('wait');
    };
    // Gán: nếu phím/nút đã dùng cho thao tác khác thì đổi chỗ cho nhau.
    const assign = (kind, action, value) => {
      const map = cfg[kind], other = Object.keys(map).find(k => k !== action && map[k] === value);
      if (other) map[other] = map[action];
      map[action] = value;
      save(cfg); wait = null; render();
    };
    host.addEventListener('click', ev => {
      const r = ev.target.closest('[data-reset]');
      if (r) { cfg[r.dataset.reset] = defaults()[r.dataset.reset]; save(cfg); wait = null; render(); return; }
      const k = ev.target.closest('.padmap [data-b]');
      if (k) {   // bấm nút trên hình = gán lại thao tác đang ở nút đó (nút trống thì bỏ qua)
        const action = Object.keys(cfg.pad).find(a => cfg.pad[a] === k.dataset.b);
        if (action) startWait('pad', action);
        return;
      }
      const b = ev.target.closest('button.bind');
      if (!b) return;
      const tr = b.closest('tr');
      startWait(tr.parentElement.dataset.kind, tr.dataset.id);
    });
    const startWait = (kind, action) => {
      render();
      wait = { kind, action, held: new Set(pressed()) };
      const nb = host.querySelector(`tbody[data-kind="${kind}"] tr[data-id="${action}"] button`);
      nb.textContent = kind === 'keys' ? 'Bấm phím… (Esc huỷ)' : 'Bấm nút trên tay cầm…';
      nb.classList.add('waiting');
      if (kind === 'pad') for (const el of host.querySelectorAll(`.padmap [data-b="${cfg.pad[action]}"]`)) el.classList.add('wait');
    };
    const onKey = ev => {
      if (wait?.kind !== 'keys') return;
      ev.preventDefault();
      if (ev.code === 'Escape') { wait = null; render(); return; }
      assign('keys', wait.action, ev.code);
    };
    addEventListener('keydown', onKey, true);

    // Tay cầm: trình duyệt chỉ báo tay cầm sau khi bấm 1 nút.
    const status = host.querySelector('.pad-status');
    const pad = () => [...(navigator.getGamepads?.() ?? [])].find(Boolean);
    const pressed = () => { const g = pad(); return g ? g.buttons.flatMap((b, i) => b.pressed ? [`b${i}`] : []) : []; };
    let raf;
    const poll = () => {
      if (!host.isConnected) { removeEventListener('keydown', onKey, true); return; }
      const g = pad(), now = pressed();
      status.textContent = g ? `Đã nhận: ${g.id}` : 'Chưa thấy tay cầm — cắm dây rồi bấm một nút bất kỳ.';
      if (wait?.kind === 'pad') {
        const fresh = now.find(b => !wait.held.has(b));
        wait.held = new Set([...wait.held].filter(b => now.includes(b)));
        if (fresh) assign('pad', wait.action, fresh);
      }
      for (const tr of host.querySelectorAll('tbody[data-kind="pad"] tr'))
        tr.classList.toggle('lit', now.includes(cfg.pad[tr.dataset.id]));
      for (const el of host.querySelectorAll('.padmap [data-b]')) el.classList.toggle('on', now.includes(el.dataset.b));
      raf = requestAnimationFrame(poll);
    };
    render();
    poll();
  }

  window.pesCtl = { load, save, streamUrl, mount };
})();
