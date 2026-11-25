"""PES17 (25/11): hình luôn là "bây giờ", không trễ dồn — và đo trễ từng người chơi.

Selkies gửi hình màn chính bằng websockets.broadcast(): không có cơ chế dừng, mạng điện thoại khựng 1 giây
thì hình dồn trong bộ đệm, người chơi thấy trễ dần rồi tua nhanh ("giật"). Người chơi P2 (vé viewer) còn
không báo nhận khung hình nên máy chủ không biết họ trễ bao nhiêu.

Vá:
- Trang chơi (pes-overlay.js) báo "PES_ACK <số khung>" khi nhận mỗi khung, "PES_STAT {json}" mỗi 5 giây.
- Máy chủ tính trễ từng người = tuổi của khung cũ nhất đã gửi mà chưa được báo nhận. Quá PES_LAG_DROP_MS
  (mặc định 220 ms) → ngừng gửi cho người đó, chờ xả hết hình cũ, xin khung đầy đủ (IDR) rồi gửi tiếp từ khung đó.
  Người chơi thấy hình đứng một nhịp rồi bắt kịp hiện tại, thay vì trễ cả giây.
- Có người phải bỏ hình → hạ bitrate chung 30% (tối thiểu PES_ABR_MIN_KBPS = 2500); 20 giây sạch → nâng 20%, tới bitrate gốc.
- Ghi số đo mỗi 5 giây/người vào /bench/stream-stats.log (JSON một dòng).
Người không có trang chơi mới (không gửi PES_ACK, ví dụ tay cầm ảo pad17) giữ nguyên cách cũ.

Chạy lại nhiều lần không sao. Có hiệu lực khi tiến trình Selkies mở lại (mất tay cầm → phải mở lại game, đừng làm giữa trận)."""
import sys
from pathlib import Path

D = Path(sys.argv[1] if len(sys.argv) > 1 else "/lsiopy/lib/python3.12/site-packages/selkies")
MARK = "pes_smooth"

HELPERS = r'''
# --- pes_smooth (PES17 25/11, tools/bench/selkies-smooth.py) ---
import collections as _pes_c, json as _pes_json, os as _pes_os, datetime as _pes_dt
PES_LAG_DROP_MS = float(_pes_os.environ.get("PES_LAG_DROP_MS", "220"))
PES_LAG_OK_MS = 60.0
PES_STATS = _pes_os.environ.get("PES_STATS", "/bench/stream-stats.log")
_pes = {}


def _pes_state(ws, perms=None):
    st = _pes.get(ws)
    if st is None:
        p = perms or {}
        st = _pes[ws] = {"sent": _pes_c.OrderedDict(), "skip": False, "idr_t": 0.0, "role": p.get("role"), "slot": p.get("slot")}
        _pes_reset(st)
    return st


def _pes_reset(st):
    st.update(t0=time.monotonic(), drops=0, skips=0, lag_sum=0.0, lag_n=0, lag_max=0.0, rtt=[], bytes=0, frames=0, keys=0)


def _pes_idr(app, st, now):
    if now - st["idr_t"] < 0.25:
        return
    st["idr_t"] = now
    try:
        cap = app.capture_instances.get("primary")
        if cap:
            cap["module"].request_idr_frame()
    except Exception as e:
        data_logger.warning(f"PES17 idr: {e}")


PES_ABR_MIN = int(_pes_os.environ.get("PES_ABR_MIN_KBPS", "2500"))
_pes_abr = {"mod": None, "base": 0, "cur": 0, "t": 0.0, "skip_t": 0.0, "bad": 0, "bad_t": 0.0}


def _pes_rate(app, now, skipped):
    """Tự hạ bitrate khi có người phải bỏ hình (mạng yếu), nâng dần lại khi 20 giây sạch. Một bộ mã hoá chung cho mọi người."""
    a = _pes_abr
    try:
        cap = app.capture_instances.get("primary")
        if not cap:
            return
        mod = cap["module"]
        ds = app.display_clients.get("primary") or {}
        fps = float(ds.get("framerate") or 60)
        if a["mod"] is not mod:   # bộ mã hoá mới (người chơi nối lại) → bắt đầu từ bitrate gốc
            a.update(mod=mod, base=int(float(ds.get("video_bitrate") or 8) * 1000), t=now, skip_t=0.0)
            a["cur"] = a["base"]
        new = a["cur"]
        if skipped:
            a["skip_t"] = now
            if now - a["t"] > 3:
                a.update(bad=a["cur"], bad_t=now)   # mức vừa hỏng: 2 phút không nâng lại tới đó
                new = max(PES_ABR_MIN, int(a["cur"] * 0.7))
        elif a["cur"] < a["base"] and now - a["skip_t"] > 20 and now - a["t"] > 20:
            new = min(a["base"], int(a["cur"] * 1.2))
            if now - a["bad_t"] < 120:
                new = min(new, int(a["bad"] * 0.85))
            new = max(new, a["cur"])
        if new != a["cur"]:
            mod.update_video_bitrate(new)
            mod.update_vbv_buf_size(max(200, int(new / fps * 2)))
            data_logger.info(f"PES17 bitrate {a['cur']} -> {new} kbps")
            a.update(cur=new, t=now)
    except Exception as e:
        data_logger.warning(f"PES17 abr: {e}")


def _pes_gate(app, viewers, chunk, frame_id):
    """Lọc người nhận khung này: ai đang trễ quá thì bỏ khung (chờ IDR)."""
    if not _pes:
        return viewers
    now = time.monotonic()
    key = len(chunk) > 1 and chunk[0] == 4 and chunk[1] == 1
    out = []
    skipped = False
    for ws in viewers:
        st = _pes.get(ws)
        if st is None:
            out.append(ws)
            continue
        sent = st["sent"]
        lag = (now - next(iter(sent.values()))) * 1000.0 if sent else 0.0
        st["lag_sum"] += lag; st["lag_n"] += 1
        if lag > st["lag_max"]:
            st["lag_max"] = lag
        if not st["skip"] and lag > PES_LAG_DROP_MS:
            st["skip"] = True
            st["skips"] += 1
            skipped = True
        if st["skip"]:
            if lag <= PES_LAG_OK_MS and key:
                st["skip"] = False
            else:
                if lag <= PES_LAG_OK_MS:
                    _pes_idr(app, st, now)
                st["drops"] += 1
                continue
        sent[frame_id] = now
        if len(sent) > 900:
            sent.popitem(last=False)
        st["bytes"] += len(chunk); st["frames"] += 1; st["keys"] += key
        out.append(ws)
    if frame_id % 30 == 0 or skipped:
        _pes_rate(app, now, skipped)
    return out


def _pes_msg(app, ws, msg, perms):
    try:
        kind, _, rest = msg.partition(" ")
        st = _pes_state(ws, perms)
        now = time.monotonic()
        if kind == "PES_ACK":
            fid = int(rest)
            sent = st["sent"]
            if fid in sent:
                while sent:
                    k, t = sent.popitem(last=False)
                    if k == fid:
                        st["rtt"].append((now - t) * 1000.0)
                        break
        elif kind == "PES_STAT":
            c = _pes_json.loads(rest) if rest else {}
            el = max(now - st["t0"], 0.001)
            r = sorted(st["rtt"])
            line = {
                "t": _pes_dt.datetime.now(_pes_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "ip": (getattr(ws, "remote_address", None) or ["?"])[0], "role": st["role"], "slot": st["slot"],
                "sent_fps": round(st["frames"] / el, 1), "kbps": round(st["bytes"] * 8 / el / 1000),
                "rtt_p50": round(r[len(r) // 2]) if r else None, "rtt_p95": round(r[int(len(r) * 0.95)]) if r else None,
                "lag_avg": round(st["lag_sum"] / st["lag_n"]) if st["lag_n"] else None, "lag_max": round(st["lag_max"]),
                "drops": st["drops"], "skips": st["skips"], "keys": st["keys"], "target_kbps": _pes_abr["cur"] or None, "client": c,
            }
            _pes_reset(st)
            with open(PES_STATS, "a") as f:
                f.write(_pes_json.dumps(line, ensure_ascii=False) + "\n")
            for w in [w for w in _pes if getattr(getattr(w, "state", None), "name", "") == "CLOSED"]:
                _pes.pop(w, None)
    except Exception as e:
        data_logger.warning(f"PES17 stat: {e}")
# --- /pes_smooth ---
'''

EDITS = [
    ('data_logger = logging.getLogger("data_websocket")\n',
     'data_logger = logging.getLogger("data_websocket")\n' + HELPERS),
    ("                elif isinstance(message, str):\n",
     "                elif isinstance(message, str):\n"
     "                    if message.startswith(\"PES_\"):  # pes_smooth: báo nhận khung + số đo từ trang chơi\n"
     "                        _pes_msg(self, websocket, message, client_permissions.get(websocket))\n"
     "                        continue\n"),
    ("                        websockets.broadcast(primary_viewers, data_chunk)\n",
     "                        websockets.broadcast(_pes_gate(self, primary_viewers, data_chunk, frame_id), data_chunk)  # pes_smooth\n"),
]


def main() -> None:
    f = D / "selkies.py"
    s = f.read_text()
    if MARK in s:
        print(f"{f.name}: đã vá")
        return
    for old, new in EDITS:
        if s.count(old) != 1:
            sys.exit(f"{f.name}: không tìm thấy đúng 1 chỗ: {old.strip()[:60]}")
        s = s.replace(old, new, 1)
    f.write_text(s)
    print(f"{f.name}: vá xong")


if __name__ == "__main__":
    main()
