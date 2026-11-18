// Thanh tỉ số đầu trang (ScoreTicker.razor): tự chạy chữ, kéo/vuốt, nút ‹ ›.
// Chạy hoàn toàn phía trình duyệt, tìm lại .ticker mỗi khung hình nên Blazor vẽ lại thanh không làm hỏng.
(() => {
  const SPEED = 30;          // px/giây
  const IDLE_MS = 5000;      // dừng tự chạy sau khi người xem chạm/kéo/bấm
  const END_PAUSE_MS = 2500; // dừng ở cuối trước khi quay về đầu
  const reduced = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
  let pausedUntil = 0, hover = false, pos = null, last = 0;
  const track = () => document.querySelector('.ticker-track');
  const hold = (ms = IDLE_MS) => { pausedUntil = Math.max(pausedUntil, performance.now() + ms); pos = null; };

  // ---- tự chạy ----
  function frame(t) {
    const tr = track(), dt = Math.min(100, t - (last || t)); last = t;
    if (tr) {
      const max = tr.scrollWidth - tr.clientWidth;
      tr.closest('.ticker')?.classList.toggle('overflow', max > 2);
      if (max > 2 && !hover && !reduced() && t > pausedUntil && !document.hidden) {
        if (pos === null || Math.abs(pos - tr.scrollLeft) > 2) pos = tr.scrollLeft;
        if (pos >= max - 1) { tr.scrollLeft = pos = 0; hold(END_PAUSE_MS); }
        else { pos = Math.min(max, pos + SPEED * dt / 1000); tr.scrollLeft = pos; if (pos >= max - 1) hold(END_PAUSE_MS); }
      }
    }
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  document.addEventListener('mouseover', e => { hover = !!e.target.closest?.('.ticker'); });
  document.addEventListener('focusin', e => { if (e.target.closest?.('.ticker')) hold(); });
  document.addEventListener('wheel', e => { if (e.target.closest?.('.ticker-track')) hold(); }, { passive: true });
  document.addEventListener('touchstart', e => { if (e.target.closest?.('.ticker-track')) hold(); }, { passive: true });

  // ---- kéo bằng chuột (cảm ứng đã cuộn sẵn) ----
  let drag = null;
  document.addEventListener('pointerdown', e => {
    const tr = e.target.closest?.('.ticker-track');
    if (!tr || e.pointerType !== 'mouse' || e.button !== 0) return;
    drag = { tr, x: e.clientX, left: tr.scrollLeft, moved: false };
    hold();
  });
  document.addEventListener('pointermove', e => {
    if (!drag) return;
    const dx = e.clientX - drag.x;
    if (Math.abs(dx) > 5) { drag.moved = true; drag.tr.classList.add('dragging'); }
    if (drag.moved) { drag.tr.scrollLeft = drag.left - dx; hold(); }
  });
  document.addEventListener('pointerup', () => {
    if (!drag) return;
    drag.tr.classList.remove('dragging');
    if (drag.moved) { // chặn cú bấm vào trận ngay sau khi kéo
      const stop = ev => { ev.preventDefault(); ev.stopPropagation(); };
      document.addEventListener('click', stop, { capture: true, once: true });
      setTimeout(() => document.removeEventListener('click', stop, { capture: true }), 0);
    }
    drag = null;
  });
  document.addEventListener('dragstart', e => { if (e.target.closest?.('.ticker-track')) e.preventDefault(); });

  // ---- nút ‹ › ----
  document.addEventListener('click', e => {
    const b = e.target.closest?.('[data-tk-nav]'); if (!b) return;
    const tr = track(); if (!tr) return;
    hold(IDLE_MS + 600);
    tr.scrollBy({ left: +b.dataset.tkNav * tr.clientWidth * 0.7, behavior: 'smooth' });
  });

})();
