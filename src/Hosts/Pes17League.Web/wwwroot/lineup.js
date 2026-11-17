// Kéo thả cầu thủ trên sân đội hình (LineupBoard, PO 05/11): pointer events → dùng được cả chuột lẫn cảm ứng.
// Kéo quá 6 px = kéo (thẻ bóng mờ theo ngón tay), thả lên thẻ cầu thủ khác → .NET Drop(from, to); thả cầu thủ sân vào chỗ trống trên sân →
// .NET Move(pid, left%, top%) (vị trí tự do, PO 05/11); chạm nhẹ = click (Blazor tự xử lý).
window.pesLineup = (() => {
  const boards = new Map();
  const attach = (root, ref) => {
    const board = root.closest('.board') || root;
    let drag = null;
    const down = e => {
      const el = e.target.closest('[data-pid]');
      if (!el || !board.contains(el) || e.button > 0 || board.classList.contains('locked')) return;
      drag = { el, x: e.clientX, y: e.clientY, ghost: null, id: e.pointerId };
    };
    const move = e => {
      if (!drag || e.pointerId !== drag.id) return;
      if (!drag.ghost) {
        if (Math.hypot(e.clientX - drag.x, e.clientY - drag.y) < 6) return;
        const r = drag.el.getBoundingClientRect();
        drag.ghost = drag.el.cloneNode(true);
        drag.ghost.classList.add('ghost');
        Object.assign(drag.ghost.style, { width: r.width + 'px', left: '0', top: '0' });
        document.body.appendChild(drag.ghost);
        drag.el.classList.add('dragging');
        drag.dx = drag.x - r.left; drag.dy = drag.y - r.top;
      }
      e.preventDefault();
      drag.ghost.style.transform = `translate(${e.clientX - drag.dx}px, ${e.clientY - drag.dy}px)`;
      board.querySelectorAll('.over').forEach(x => x.classList.remove('over'));
      const t = target(e, drag.ghost);
      if (t && t !== drag.el) t.classList.add('over');
    };
    const target = (e, ghost) => {
      ghost.style.display = 'none';
      const t = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-pid]');
      ghost.style.display = '';
      return t && board.contains(t) ? t : null;
    };
    const up = e => {
      if (!drag || e.pointerId !== drag.id) return;
      const d = drag; drag = null;
      if (!d.ghost) return;
      const t = target(e, d.ghost);
      d.ghost.remove(); d.el.classList.remove('dragging');
      board.querySelectorAll('.over').forEach(x => x.classList.remove('over'));
      // Sau khi kéo trình duyệt vẫn bắn click lên thẻ gốc → chặn 1 lần để không thành "chạm chọn".
      // Cảm ứng thường không bắn click → bỏ chặn sau 300 ms để không nuốt lần chạm sau.
      const eat = ev => { ev.stopPropagation(); ev.preventDefault(); };
      window.addEventListener('click', eat, { capture: true, once: true });
      setTimeout(() => window.removeEventListener('click', eat, true), 300);
      if (t && t !== d.el) { ref.invokeMethodAsync('Drop', d.el.dataset.pid, t.dataset.pid); return; }
      // Thả cầu thủ trên sân vào chỗ trống → vị trí tự do (tâm thẻ, % theo khung sân). Ô thủ môn không di chuyển.
      const pitch = board.querySelector('.pitch');
      if (t || !pitch || !pitch.contains(d.el) || d.el.dataset.slot === '0') return;
      const r = pitch.getBoundingClientRect(), w = d.el.offsetWidth, h = d.el.offsetHeight;
      const cx = e.clientX - d.dx + w / 2, cy = e.clientY - d.dy + h / 2;
      if (cx < r.left || cx > r.right || cy < r.top || cy > r.bottom) return;
      ref.invokeMethodAsync('Move', d.el.dataset.pid, (cx - r.left) / r.width * 100, (cy - r.top) / r.height * 100);
    };
    board.addEventListener('pointerdown', down);
    window.addEventListener('pointermove', move, { passive: false });
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
    boards.set(root, () => {
      board.removeEventListener('pointerdown', down);
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      window.removeEventListener('pointercancel', up);
    });
  };
  const detach = root => { boards.get(root)?.(); boards.delete(root); };
  return { attach, detach };
})();
