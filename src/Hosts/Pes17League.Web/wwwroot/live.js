// Xem trực tiếp (Live.razor, PO 07/11): phát HLS /truc-tiep/hls/{trận}.m3u8. Safari/iOS phát thẳng; trình duyệt khác nạp hls.js
// (lib/hls, 1.7.3, Apache-2.0) lúc cần. Playlist chưa có (ffmpeg chưa ghi đoạn đầu, ~2–4 s) / lỗi mạng → thử lại sau 2 s.
window.pesLive = (() => {
  let hls = null, el = null, url = null, timer = null;
  const load = () => window.Hls ? Promise.resolve() : new Promise((ok, fail) => {
    const s = document.createElement('script');
    s.src = 'lib/hls/hls.min.js'; s.onload = ok; s.onerror = fail;
    document.head.appendChild(s);
  });
  // Phát có tiếng bị chặn khi người xem chưa chạm trang (vd đã bật loa rồi tự kết nối lại) → tắt tiếng rồi phát
  const go = v => v.play().catch(e => { if (e.name === 'NotAllowedError' && !v.muted) { v.muted = true; v.play().catch(() => {}); } });

  function stop() {
    clearTimeout(timer); timer = null;
    if (hls) { hls.destroy(); hls = null; }
    if (el) { el.onerror = null; el.removeAttribute('src'); el.load(); }
    el = url = null;
  }

  function retry() {
    if (!el) return;
    const v = el, u = url;
    if (hls) { hls.destroy(); hls = null; }
    clearTimeout(timer);
    timer = setTimeout(() => { if (el === v && url === u) start(v, u); }, 2000);
  }

  async function start(v, u) {
    el = v; url = u;
    if (v.canPlayType('application/vnd.apple.mpegurl')) {
      v.onerror = retry; v.src = u; go(v);
      return;
    }
    try { await load(); } catch { retry(); return; }
    if (el !== v || url !== u || !Hls.isSupported()) return;   // đã đổi trận / rời trang trong lúc nạp
    // 4 đoạn × 2 giây ≈ 8 giây sau live. Bám 2 đoạn thì một đoạn tới trễ làm hình đứng.
    hls = new Hls({ liveSyncDurationCount: 4, liveMaxLatencyDurationCount: 8 });
    hls.on(Hls.Events.ERROR, (_, d) => { if (d.fatal) retry(); });
    hls.on(Hls.Events.MANIFEST_PARSED, () => go(v));
    hls.loadSource(u);
    hls.attachMedia(v);
  }

  // Trận mới: tự phát tắt tiếng (luật trình duyệt; <video muted> do Blazor tạo bằng JS chỉ bật defaultMuted, thử 07/11), người xem bấm loa
  // để nghe; thử lại (retry → start) giữ nguyên lựa chọn đó.
  return { play(v, u) { stop(); v.muted = true; start(v, u); }, stop };
})();
