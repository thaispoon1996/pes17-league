// Trải nghiệm chung (PO 17/11 "làm lại UI/UX mượt, chuyên nghiệp"): không sửa nghiệp vụ, chỉ lớp giao diện.
// 1) Nút đang xử lý: bấm nút Blazor → khoá tạm + vòng xoay (sau 250ms), chặn bấm đúp; mở khoá khi giao diện cập nhật.
// 2) Thông báo nổi: khối .msg xuất hiện/đổi chữ ngay sau 1 cú bấm → hiện thêm toast góc màn hình.
// 3) Hỏi lại: nút có data-confirm="…" → hộp xác nhận riêng (không dùng confirm() của trình duyệt).
// 4) Thanh tiến trình khi chuyển trang; 5) mất kết nối: dải báo nhỏ, server khởi động lại → tự tải lại trang.
(() => {
  const $ = (t, c, h) => { const e = document.createElement(t); if (c) e.className = c; if (h != null) e.textContent = h; return e; };
  let lastClick = 0;

  /* ---------- 3) Hộp xác nhận ---------- */
  let confirmed = null;
  function ask(btn) {
    const box = $('div', 'ui-dialog'), card = $('div', 'ui-dialog__card');
    card.setAttribute('role', 'alertdialog'); card.setAttribute('aria-modal', 'true');
    const danger = btn.classList.contains('danger') || btn.dataset.confirmDanger != null;
    card.append($('h3', null, btn.dataset.confirmTitle || 'Xác nhận'), $('p', null, btn.dataset.confirm));
    const row = $('div', 'ui-dialog__actions'), no = $('button', null, 'Huỷ'), yes = $('button', danger ? 'danger' : 'primary', btn.dataset.confirmOk || 'Đồng ý');
    no.type = yes.type = 'button';
    row.append(no, yes); card.append(row); box.append(card); document.body.append(box);
    requestAnimationFrame(() => box.classList.add('on'));
    const close = () => { box.classList.remove('on'); setTimeout(() => box.remove(), 180); document.removeEventListener('keydown', key, true); };
    const key = e => { if (e.key === 'Escape') { e.preventDefault(); close(); } };
    document.addEventListener('keydown', key, true);
    no.onclick = close;
    box.addEventListener('click', e => { if (e.target === box) close(); });
    yes.onclick = () => { close(); confirmed = btn; btn.click(); };
    yes.focus();
  }

  /* ---------- 1) Nút đang xử lý ---------- */
  const busy = new Set();
  function settle(btn) { btn.classList.remove('is-busy'); btn.removeAttribute('aria-busy'); busy.delete(btn); }
  window.addEventListener('click', e => {
    const btn = e.target.closest && e.target.closest('button, .btn');
    lastClick = Date.now();
    if (!btn || btn.disabled) return;
    if (btn.dataset.confirm && confirmed !== btn) { e.preventDefault(); e.stopImmediatePropagation(); ask(btn); return; }
    confirmed = null;
    if (busy.has(btn)) { e.preventDefault(); e.stopImmediatePropagation(); return; }   // bấm đúp
    if (btn.tagName !== 'BUTTON' || btn.closest('.ui-dialog, .no-busy') || btn.classList.contains('no-busy')) return;
    if (btn.type === 'submit' && btn.form && btn.form.dataset.enhance === 'false') return;   // form thường (đăng xuất…) tự tải trang
    busy.add(btn); btn.classList.add('is-busy'); btn.setAttribute('aria-busy', 'true');
    setTimeout(() => settle(btn), 10000);
  }, true);

  /* ---------- 2) Toast ---------- */
  let tray;
  function toast(text, kind) {
    if (!text) return;
    if (!tray) { tray = $('div', 'ui-toasts'); tray.setAttribute('aria-live', 'polite'); document.body.append(tray); }
    const t = $('div', 'ui-toast ' + kind); t.append($('i', null, kind === 'ok' ? '✓' : kind === 'err' ? '!' : 'i'), $('span', null, text));
    const x = $('button', 'ui-toast__x no-busy', '×'); x.type = 'button'; x.setAttribute('aria-label', 'Đóng'); t.append(x);
    const go = () => { t.classList.remove('on'); setTimeout(() => t.remove(), 220); };
    x.onclick = go; tray.append(t); requestAnimationFrame(() => t.classList.add('on'));
    setTimeout(go, kind === 'err' ? 7000 : 4000);
    while (tray.children.length > 3) tray.firstChild.remove();
  }
  const bad = /(lỗi|không (thể|đủ|được|hợp lệ|tìm)|chưa (đủ|có|được)|sai |thất bại|vượt|hết hạn|bị (khoá|chặn|từ chối))/i;
  const seen = new WeakMap();
  function checkMsg(el) {
    const text = el.textContent.trim().replace(/\s+/g, ' ');
    if (!text || seen.get(el) === text) return;
    seen.set(el, text);
    if (Date.now() - lastClick > 4000) return;   // khối ghi chú tĩnh, không phải phản hồi thao tác
    toast(text.length > 180 ? text.slice(0, 177) + '…' : text, el.classList.contains('ok') ? 'ok' : bad.test(text) ? 'err' : 'info');
  }

  /* ---------- 4) Thanh tiến trình chuyển trang ---------- */
  const bar = $('div', 'ui-progress');
  let barTimer;
  function startBar() { clearTimeout(barTimer); bar.classList.remove('done'); bar.classList.add('run'); barTimer = setTimeout(endBar, 8000); }
  function endBar() { if (!bar.classList.contains('run')) return; bar.classList.add('done'); setTimeout(() => bar.classList.remove('run', 'done'), 300); }
  document.addEventListener('click', e => {
    const a = e.target.closest && e.target.closest('a[href]');
    if (!a || e.defaultPrevented || e.ctrlKey || e.metaKey || e.shiftKey || a.target || a.hasAttribute('download')) return;
    const u = new URL(a.href, location.href);
    if (u.origin !== location.origin || (u.pathname === location.pathname && u.search === location.search)) return;
    startBar();
    document.querySelectorAll('details[open].nav-more, details[open].m-menu').forEach(d => d.open = false);   // đóng menu khi chọn mục
  });

  // Menu ☰ / Thêm: bấm ra ngoài hoặc Esc để đóng
  document.addEventListener('click', e => document.querySelectorAll('details[open].m-menu, details[open].nav-more')
    .forEach(d => { if (e.target === d || !d.contains(e.target)) d.open = false; }));
  document.addEventListener('keydown', e => e.key === 'Escape' && document.querySelectorAll('details[open].m-menu, details[open].nav-more').forEach(d => d.open = false));

  // Chuyển nhượng trên điện thoại: chạm 1 cầu thủ → cuộn tới hồ sơ trinh sát (nằm dưới danh sách)
  document.addEventListener('click', e => {
    if (innerWidth > 1000 || !e.target.closest('.fm-search tbody tr')) return;
    setTimeout(() => { const s = document.querySelector('.fm-scout'); s && s.scrollIntoView({ behavior: 'smooth', block: 'start' }); }, 250);
  });
  // Nút nổi "Vào trận ↓": ẩn khi khu Vào trận đã hiện trên màn hình
  const cta = () => { const b = document.querySelector('.jump-cta'), t = document.getElementById('vao-tran'); if (!b || !t) return;
    b.classList.toggle('off', t.getBoundingClientRect().top < innerHeight - 80); };
  addEventListener('scroll', cta, { passive: true });

  /* ---------- theo dõi DOM ---------- */
  let settleTimer, subTimer;
  new MutationObserver(list => {
    for (const m of list) {
      const t = m.target.nodeType === 1 ? m.target : m.target.parentElement;
      if (!t || t.closest('.ui-toasts, .ui-dialog, .ticker')) continue;
      t.closest('.msg') && checkMsg(t.closest('.msg'));
      for (const n of m.addedNodes) if (n.nodeType === 1) {
        if (n.matches('.msg')) checkMsg(n);
        n.querySelectorAll && n.querySelectorAll('.msg').forEach(checkMsg);
      }
    }
    if (busy.size) { clearTimeout(settleTimer); settleTimer = setTimeout(() => busy.forEach(settle), 150); }
    endBar(); cta();
    clearTimeout(subTimer); subTimer = setTimeout(() => document.querySelectorAll('.subnav a.active').forEach(a => {
      const n = a.parentElement; if (n.scrollWidth > n.clientWidth) n.scrollTo({ left: a.offsetLeft - 12, behavior: 'smooth' }); }), 200);
  }).observe(document.body, { childList: true, subtree: true, characterData: true });
  window.addEventListener('popstate', startBar);
  document.querySelectorAll('.msg').forEach(el => seen.set(el, el.textContent.trim().replace(/\s+/g, ' ')));

  /* ---------- 5) Mất kết nối ---------- */
  document.addEventListener('DOMContentLoaded', () => document.body.append(bar));
  if (document.body) document.body.append(bar);
  const modal = () => document.getElementById('components-reconnect-modal');
  new MutationObserver(() => {
    const m = modal(); if (!m) return;
    if (m.classList.contains('components-reconnect-rejected')) location.reload();   // web vừa cập nhật → phiên cũ hết hạn
  }).observe(document.documentElement, { attributes: true, subtree: true, attributeFilter: ['class'] });
  // Mất hẳn kết nối: tự thử tải lại khi quay lại tab / có mạng
  const retry = () => { const m = modal(); if (m && m.classList.contains('components-reconnect-failed')) location.reload(); };
  document.addEventListener('visibilitychange', () => document.visibilityState === 'visible' && retry());
  window.addEventListener('online', retry);
  window.pesUi = { toast };
})();

/* Đếm ngược hạn vòng ở trang chủ (Stitch 17/11): <span data-countdown="epoch ms">. */
(function () {
  function fmt(ms) {
    if (ms < 0) ms = 0;
    var s = Math.floor(ms / 1000), d = Math.floor(s / 86400), h = Math.floor(s % 86400 / 3600), m = Math.floor(s % 3600 / 60), x = s % 60;
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d > 0 ? d + 'n ' + p(h) + ':' + p(m) : p(h) + ':' + p(m) + ':' + p(x);
  }
  setInterval(function () {
    var els = document.querySelectorAll('[data-countdown]');
    for (var i = 0; i < els.length; i++) els[i].textContent = fmt(+els[i].getAttribute('data-countdown') - Date.now());
  }, 1000);
})();
