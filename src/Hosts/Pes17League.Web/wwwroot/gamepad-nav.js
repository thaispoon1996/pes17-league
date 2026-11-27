// Điều khiển menu web bằng tay cầm kiểu Master League (PO 27/11): máy tính + điện thoại.
// D-pad/cần trái: di chuyển ô chọn theo hướng · A/✕ chọn · B/○ quay lại · LB/RB đổi khu · LT/RT đổi mục con
// Y/△ chuông việc cần xử lý · Start về Trang chủ · cần phải cuộn.
// Chỉ bật khi bấm tay cầm; chuột/chạm/bàn phím → tự ẩn. Không chạy ở trang chơi (/vao-tran) và đổi phím (/dieu-khien).
(() => {
  if (!('getGamepads' in navigator)) return;
  const OFF = /^\/(vao-tran|dieu-khien)(\/|$)/;
  const SEL = 'a[href], button:not([disabled]), summary, input:not([type=hidden]):not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
  const CHROME = '.cxh, .cx-dock';
  const html = document.documentElement;
  let on = false, cur = null, ring = null, hint = null, href = location.href, kind = '', raf = 0;
  const held = {};          // nút logic → { t: lần bắn kế tiếp }
  const reduce = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
  const fire = n => { try { window.cxAudio?.fire(n); } catch { } };

  /* ---------- Phần tử chọn được ---------- */
  function shown(el) {
    if (!el.isConnected || el.closest('[aria-hidden="true"], [inert], .lock, [aria-disabled="true"]')) return false;
    const r = el.getBoundingClientRect();
    if (r.width < 2 || r.height < 2) return false;
    if (el.checkVisibility) return el.checkVisibility({ visibilityProperty: true, checkVisibilityCSS: true, opacityProperty: true, checkOpacity: true });
    const cs = getComputedStyle(el); return cs.visibility !== 'hidden' && +cs.opacity > 0;
  }
  const vis = sel => [...document.querySelectorAll(sel)].find(shown);
  // Vùng đang mở chặn phần còn lại (hộp xác nhận → lớp phủ → bảng mục con → bảng trượt → menu thả xuống).
  function scope() {
    return vis('.ui-dialog.on') || vis('.cx-ov') || vis('#cx-menu[data-open] .cx-msheet[data-open]')
      || vis('.cx .mk-sheet, .cx .bx-sheet') || vis('[role=dialog][aria-modal=true]')
      || [...document.querySelectorAll('details[open].nav-more, details[open].m-menu')].pop() || document.body;
  }
  // Bỏ ô nằm ngoài màn hình theo chiều ngang (dải chạy chữ…) trừ khi nằm trong khung cuộn ngang được
  const offX = el => { const r = el.getBoundingClientRect(); return (r.right < 4 || r.left > innerWidth - 4) && !scroller(el, 0); };
  const items = sc => [...sc.querySelectorAll(SEL)].filter(el => shown(el) && !el.closest('.gp-skip, #gp-hint') && !offX(el));
  const inView = r => r.bottom > 0 && r.top < innerHeight;
  function first(sc) {
    const list = items(sc);
    if (sc === document.body) {
      const main = list.filter(el => el.closest('.cx-page, main'));
      const seen = main.find(el => inView(el.getBoundingClientRect()));
      if (seen || main.length) return seen || main[0];
    }
    return list.find(el => inView(el.getBoundingClientRect())) || list[0] || null;
  }

  /* ---------- Con trỏ xanh ---------- */
  function ensureUi() {
    if (ring?.isConnected) return;
    ring = document.createElement('div'); ring.id = 'gp-ring'; ring.setAttribute('aria-hidden', 'true');
    hint = document.createElement('div'); hint.id = 'gp-hint'; hint.setAttribute('aria-hidden', 'true');
    document.body.append(ring, hint);
    drawHint();
  }
  function drawHint() {
    if (!hint) return;
    const ps = kind === 'ps';
    const k = (b, t, c = '') => `<span class="${c}"><kbd class="gp-k ${ps ? 'ps' : ''}">${b}</kbd>${t}</span>`;
    hint.innerHTML = k(ps ? '✕' : 'A', 'Chọn') + k(ps ? '○' : 'B', 'Quay lại') + k(ps ? 'L1 R1' : 'LB RB', 'Khu') +
      k(ps ? 'L2 R2' : 'LT RT', 'Mục', 'gp-x') + k(ps ? '△' : 'Y', 'Thông báo', 'gp-x') + k('☰', 'Trang chủ', 'gp-x');
  }
  function place() {
    if (!ring) return;
    if (!on || !cur || !shown(cur)) { ring.classList.remove('on'); return; }
    const r = cur.getBoundingClientRect(), pad = 3;
    const br = Math.max(8, parseFloat(getComputedStyle(cur).borderTopLeftRadius) || 0);
    ring.style.cssText = `transform:translate(${r.left - pad}px,${r.top - pad}px);width:${r.width + pad * 2}px;height:${r.height + pad * 2}px;border-radius:${br + pad}px`;
    ring.classList.add('on');
    const dock = vis('.cx-dock');
    html.style.setProperty('--gp-bottom', (dock ? dock.getBoundingClientRect().height + 10 : 14) + 'px');
  }
  function select(el, sound = true) {
    if (!el) return;
    cur?.classList.remove('gp-cur');
    cur = el; el.classList.add('gp-cur');
    try { el.focus({ preventScroll: true }); } catch { }
    const r = el.getBoundingClientRect();
    const top = (vis('.cxh')?.getBoundingClientRect().bottom ?? 0) + (vis('.cx-sub')?.getBoundingClientRect().height ?? 0);
    const bottom = innerHeight - (vis('.cx-dock')?.getBoundingClientRect().height ?? 0) - 56;
    if (r.top < Math.max(0, top) + 8 || r.bottom > bottom) el.scrollIntoView({ block: 'center', inline: 'nearest', behavior: reduce() ? 'auto' : 'smooth' });
    if (sound) fire('click');
  }
  const valid = () => cur && cur.isConnected && shown(cur) && scope().contains(cur);

  /* ---------- Di chuyển theo hướng ---------- */
  function move(dx, dy) {
    const sc = scope();
    if (!valid()) { select(first(sc)); return; }
    const a = cur.getBoundingClientRect(), ax = a.left + a.width / 2, ay = a.top + a.height / 2;
    let best = null, bs = Infinity;
    const inChrome = !!cur.closest(CHROME);   // đầu trang / thanh dưới: chỉ đi vào khi đang đứng ở đó (LB/RB, Y lo phần này)
    for (const el of items(sc)) {
      if (el === cur || el.contains(cur) || cur.contains(el) || !inChrome && el.closest(CHROME)) continue;
      const r = el.getBoundingClientRect(), cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      if ((dx ? (cx - ax) * dx : (cy - ay) * dy) <= 2) continue;
      const gap = dx ? (dx > 0 ? r.left - a.right : a.left - r.right) : (dy > 0 ? r.top - a.bottom : a.top - r.bottom);
      if (gap < -Math.min(dx ? a.width : a.height, dx ? r.width : r.height) / 2) continue;
      const ortho = dx ? Math.max(0, r.top - a.bottom, a.top - r.bottom) : Math.max(0, r.left - a.right, a.left - r.right);
      const s = Math.max(gap, 0) + ortho * 3 + Math.abs(dx ? cy - ay : cx - ax) * .25;
      if (s < bs) { bs = s; best = el; }
    }
    if (best) { select(best); return; }
    if (dy) scrollAt(cur, 0, dy * innerHeight * .45, true);   // hết ô theo hướng đó → cuộn tiếp
  }
  function scroller(el, axis) {
    for (let p = el?.parentElement; p && p !== document.body; p = p.parentElement) {
      const cs = getComputedStyle(p), ov = axis ? cs.overflowY : cs.overflowX;
      if (/(auto|scroll)/.test(ov) && (axis ? p.scrollHeight > p.clientHeight + 2 : p.scrollWidth > p.clientWidth + 2)) return p;
    }
    return null;
  }
  function scrollAt(el, x, y, smooth) {
    const b = smooth && !reduce() ? 'smooth' : 'auto';
    if (x) (scroller(el, 0) || window).scrollBy({ left: x, behavior: b });
    if (y) (scroller(el, 1) || window).scrollBy({ top: y, behavior: b });
  }

  /* ---------- Nút ---------- */
  function press() {
    if (!valid()) { select(first(scope())); return; }
    const el = cur;
    if (el.matches('input:not([type=checkbox]):not([type=radio]):not([type=button]):not([type=submit]), textarea')) { el.focus(); return; }
    if (el.matches('select')) { el.focus(); try { el.showPicker(); } catch { } return; }
    el.click();
    // Mở menu thả xuống / bảng → nhảy vào mục đầu
    if (el.matches('summary')) setTimeout(() => { const d = el.parentElement; if (d?.open) select(items(d).find(x => x !== el) || el, false); }, 60);
    else if (el.matches('[data-sheet]')) setTimeout(() => select(first(scope()), false), 120);
  }
  function back() {
    const dlg = vis('.ui-dialog.on');
    if (dlg) { [...dlg.querySelectorAll('button')].find(b => !b.matches('.primary, .danger'))?.click(); later(); return; }
    const ov = vis('.cx-ov');
    if (ov) { (ov.querySelector('button.ghost') || ov.querySelector('button'))?.click(); later(); return; }
    const m = document.querySelector('#cx-menu[data-open]');
    if (m) { const id = m.getAttribute('data-open'); m.querySelector('[data-sheet=close]')?.click(); select(vis(`.cx-dock [data-sheet="${id}"]`), false); fire('nav'); return; }
    const sh = vis('.cx .mk-sheet, .cx .bx-sheet');
    if (sh) { sh.querySelector('.x')?.click(); later(); return; }
    const d = [...document.querySelectorAll('details[open].nav-more, details[open].m-menu')].pop();
    if (d) { d.open = false; select(d.querySelector('summary'), false); fire('nav'); return; }
    if (history.length > 1) { history.back(); fire('nav'); }
  }
  // Đổi trang: chờ trang mới có ô trong .cx-page rồi đặt con trỏ vào ô đầu (thử lại tới ~2 s)
  function settle(n) {
    setTimeout(() => {
      if (!on || valid() && !cur.closest('.cxh, .cx-dock')) return;
      const sc = scope(), f = first(sc);
      if (sc === document.body && !(f && f.closest('.cx-page, main')) && n < 8) { settle(n + 1); return; }
      select(f, false);
    }, 250);
  }
  const later = () => setTimeout(() => { if (!valid()) select(first(scope()), false); }, 260);
  const closeAll = () => {
    document.querySelectorAll('details[open].nav-more, details[open].m-menu').forEach(d => d.open = false);
  };
  // LB/RB: các khu (máy tính: menu chính đầu trang; điện thoại: thanh dưới)
  function zone(step) {
    const desk = [...document.querySelectorAll('.cxh-nav > a, .cxh-nav > details > summary')].filter(shown);
    const dock = [...document.querySelectorAll('.cx-dock > a, .cx-dock > button')].filter(shown);
    const list = desk.length ? desk : dock;
    if (!list.length) return;
    let i;
    if (desk.length) {
      i = list.findIndex(x => x.parentElement.open);
      if (i < 0) i = list.findIndex(x => x.matches('.active') || x.parentElement.matches('details.on'));
    } else {
      const open = document.querySelector('#cx-menu')?.getAttribute('data-open');
      i = open ? list.findIndex(x => x.dataset.sheet === open) : list.findIndex(x => x.matches('.on'));
    }
    const t = list[((i < 0 ? (step > 0 ? -1 : 0) : i) + step + list.length) % list.length];
    fire('nav');
    if (desk.length) {
      closeAll();
      if (t.tagName === 'A') { t.click(); return; }
      t.click();   // mở menu thả xuống (Blazor nạp số liệu khu)
      setTimeout(() => { const d = t.parentElement; if (!d.open) d.open = true; select(items(d).find(x => x !== t) || t, false); }, 60);
    } else {
      const m = document.querySelector('#cx-menu[data-open]');
      if (t.tagName === 'A') { m?.querySelector('[data-sheet=close]')?.click(); t.click(); return; }
      t.click();
      setTimeout(() => select(first(scope()) || t, false), 120);
    }
  }
  // LT/RT: mục con của khu (thanh viên thuốc .cx-sub)
  function sub(step) {
    const list = [...document.querySelectorAll('.cx-sub a')].filter(shown);
    if (!list.length) return;
    const i = list.findIndex(x => x.matches('.active'));
    const t = list[i < 0 ? 0 : (i + step + list.length) % list.length];
    if (t === list[i]) return;
    fire('nav'); t.click(); select(t, false);
  }
  function bell() {
    const s = vis('.cxh-bell > summary'); if (!s) return;
    const m = document.querySelector('#cx-menu[data-open]'); m?.querySelector('[data-sheet=close]')?.click();
    const d = s.parentElement, was = d.open;
    closeAll(); if (was) { select(s, false); return; }
    d.open = true; fire('nav');
    setTimeout(() => select(items(d).find(x => x !== s) || s, false), 40);
  }
  function home() {
    closeAll(); document.querySelector('#cx-menu[data-open] [data-sheet=close]')?.click();
    const a = vis('.cxh-nav > a[href="su-nghiep"]') || vis('.cx-dock > a[href="su-nghiep"]') || vis('.cxh-club');
    fire('nav');
    if (a) a.click(); else location.href = '/';
  }

  /* ---------- Bật / tắt ---------- */
  function activate(pad) {
    const k = /054c|playstation|dualsense|dualshock/i.test(pad.id) || (/wireless controller/i.test(pad.id) && !/xbox/i.test(pad.id)) ? 'ps' : 'xbox';
    if (k !== kind) { kind = k; drawHint(); }
    if (on) return false;
    on = true; ensureUi(); html.classList.add('gp-on');
    if (!valid()) select(first(scope()), false);
    return true;
  }
  function deactivate() {
    if (!on) return;
    on = false; html.classList.remove('gp-on'); cur?.classList.remove('gp-cur'); place();
  }
  let mx = -1, my = -1;
  addEventListener('mousemove', e => { if (Math.abs(e.screenX - mx) + Math.abs(e.screenY - my) > 6 && mx >= 0) deactivate(); mx = e.screenX; my = e.screenY; }, { passive: true });
  ['pointerdown', 'touchstart', 'wheel'].forEach(t => addEventListener(t, e => { if (e.isTrusted) deactivate(); }, { passive: true, capture: true }));
  addEventListener('keydown', e => { if (e.isTrusted) deactivate(); }, true);

  /* ---------- Vòng đọc tay cầm ---------- */
  const DIRS = { up: [0, -1], down: [0, 1], left: [-1, 0], right: [1, 0] };
  function btn(p, i) { const b = p.buttons[i]; return !!b && (b.pressed || b.value > .5); }
  function tick(now) {
    raf = requestAnimationFrame(tick);
    if (location.href !== href) { href = location.href; const tab = cur?.closest('.cx-sub'); cur?.classList.remove('gp-cur'); cur = null; if (on) tab ? setTimeout(() => select(vis('.cx-sub a.active') || first(scope()), false), 300) : settle(0); }
    const pads = [...(navigator.getGamepads?.() ?? [])].filter(Boolean);
    if (!pads.length || document.hidden || OFF.test(location.pathname)) { if (OFF.test(location.pathname)) deactivate(); place(); return; }
    // Gộp mọi tay cầm đang cắm
    const st = {}; let lx = 0, ly = 0, rx = 0, ry = 0, act = null;
    for (const p of pads) {
      const ax = p.axes;
      const map = { a: btn(p, 0), b: btn(p, 1), y: btn(p, 3), lb: btn(p, 4), rb: btn(p, 5), lt: btn(p, 6), rt: btn(p, 7), start: btn(p, 9),
        up: btn(p, 12) || ax[1] < -.55, down: btn(p, 13) || ax[1] > .55, left: btn(p, 14) || ax[0] < -.55, right: btn(p, 15) || ax[0] > .55 };
      for (const k in map) if (map[k]) { st[k] = true; act = p; }
      if (Math.abs(ax[2] ?? 0) > .25) { rx = ax[2]; act = p; }
      if (Math.abs(ax[3] ?? 0) > .25) { ry = ax[3]; act = p; }
    }
    if (act && activate(act)) { for (const k in st) held[k] = { t: Infinity }; place(); return; }   // lần bấm đầu chỉ hiện con trỏ
    if (on && (rx || ry)) { const f = v => Math.sign(v) * Math.pow(Math.abs(v), 2) * 26; scrollAt(cur, f(rx), f(ry), false); }
    for (const k of ['a', 'b', 'y', 'lb', 'rb', 'lt', 'rt', 'start', 'up', 'down', 'left', 'right']) {
      if (!st[k]) { delete held[k]; continue; }
      const h = held[k];
      if (h && now < h.t) continue;
      const dir = DIRS[k];
      held[k] = { t: dir ? now + (h ? 110 : 380) : Infinity };   // giữ hướng → lặp, nút khác chỉ 1 lần
      if (!on) continue;
      if (dir) move(...dir);
      else ({ a: press, b: back, y: bell, lb: () => zone(-1), rb: () => zone(1), lt: () => sub(-1), rt: () => sub(1), start: home })[k]();
    }
    place();
  }
  const start = () => { if (!raf) raf = requestAnimationFrame(tick); };
  const stop = () => { if (![...(navigator.getGamepads?.() ?? [])].some(Boolean)) { cancelAnimationFrame(raf); raf = 0; deactivate(); } };
  addEventListener('gamepadconnected', start);
  addEventListener('gamepaddisconnected', () => setTimeout(stop, 50));
  addEventListener('scroll', () => on && place(), { passive: true, capture: true });
  addEventListener('resize', () => on && place());
  if ([...(navigator.getGamepads?.() ?? [])].some(Boolean)) start();
})();
