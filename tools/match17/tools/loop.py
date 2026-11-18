#!/usr/bin/env python3
"""Vòng trận PES17: đá hết hiệp, xác nhận, rồi Rematch.

Chạy nền đến khi dừng process. Mỗi ~2s chụp một ảnh (ghi đè screens/loop_live.png).

Màn → bấm (xem classify):
  stats        Half Time / Time Up → A
  result       hết hiệp → A (2nd Half); hết trận → ghi sổ + pick.py; Pause Menu → B
  lineup       → B
  deadball     → A (lưới an toàn; Coach Mode thì không xảy ra)
  setpiece     kẹt ≥ 2 nhịp → B
  replay_ctl   > 4s → B
  hud / other  không bấm (không còn nudge A, không Start khi thấy video)
"""
from __future__ import annotations

import json, subprocess, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import score

SPIKE = score.SPIKE
PAD = SPIKE / "tools" / "pad.sh"
SHOT = SPIKE / "tools" / "shot.sh"
LOG = SPIKE / "events" / "loop.jsonl"
LIVE = SPIKE / "screens" / "loop_live.png"
PHASE_PATH = SPIKE / "events" / "phase.txt"


def grab():
    last = None
    for _ in range(2):
        last = subprocess.run([str(SHOT), "loop_live"], check=False, capture_output=True)
        if last.returncode == 0:
            return score.Image.open(LIVE).convert("RGB")
        time.sleep(0.4)
    raise subprocess.CalledProcessError(last.returncode, last.args, last.stdout, last.stderr)


def yellow_e(im) -> int:
    n = 0
    for y in range(250, 560, 3):
        for x in range(40, 280, 3):
            r, g, b = im.getpixel((x, y))
            if r > 180 and g > 150 and b < 80:
                n += 1
    return n


def white_bar(im) -> int:
    n = 0
    for y in range(630, 690, 2):
        for x in range(80, 1200, 4):
            if sum(im.getpixel((x, y))[:3]) > 700:
                n += 1
    return n


def start_match_bar(im) -> bool:
    """Hàng sáng 'Start the Match' trên hộp Rematch."""
    best = 0
    for y in range(180, 250, 4):
        xs = [x for x in range(120, 470, 2) if sum(im.getpixel((x, y))[:3]) > 560]
        if len(xs) > 80 and xs[-1] - xs[0] > 200:
            best = max(best, len(xs))
    return best > 80


def green_pitch(im) -> int:
    n = 0
    for y in range(220, 620, 4):
        for x in range(180, 1100, 4):
            r, g, b = im.getpixel((x, y))
            if g > 80 and g > r + 25 and g > b + 15:
                n += 1
    return n


def mid_white(im) -> int:
    return sum(
        1
        for x in range(480, 800)
        for y in range(200, 320)
        if sum(im.getpixel((x, y))[:3]) > 480
    )


def replay_bar(im) -> int:
    """Thanh điều khiển tối dưới video (cả lúc cận bóng, không thấy lưới)."""
    n = 0
    for y in range(600, 700, 2):
        for x in range(40, 900, 4):
            r, g, b = im.getpixel((x, y))
            if r < 50 and g < 55 and b < 70 and abs(r - g) < 15:
                n += 1
    return n


def replay_net(im) -> int:
    """Lưới khung thành trắng lúc chiếu lại bàn thắng. Sân thường nằm dưới 700."""
    n = 0
    w, _h = im.size
    for y in range(130, 520, 3):
        for x in range(40, 900, 3):
            r, g, b = im.getpixel((x, y))
            if r > 190 and g > 190 and b > 190:
                r2, g2, _b2 = im.getpixel((min(w - 1, x + 5), y))
                if r2 < 120 and g2 < 140:
                    n += 1
    return n


def confirm_icon(im) -> int:
    """Nút A xanh góc dưới của bảng Half Time / Time Up."""
    n = 0
    for y in range(662, 690):
        for x in range(95, 130):
            r, g, b = im.getpixel((x, y))[:3]
            if g > 160 and g > r + 40 and b < 80:
                n += 1
    return n


def help_guide(im) -> bool:
    """Bảng tối chữ trắng góc phải trên (Position Shift / Display Help Guides / Camera):
    bóng chết của đội có tay cầm, game đứng chờ người bấm. Đo 17/11: chờ phát bóng
    dark≈4100 white≈200; sân đang đá dark<700; Replay Control white=0."""
    dark = white = 0
    for y in range(98, 190, 2):
        for x in range(892, 1210, 3):
            r, g, b = im.getpixel((x, y))[:3]
            if max(r, g, b) < 60:
                dark += 1
            elif min(r, g, b) > 200:
                white += 1
    return dark > 3000 and 120 < white < 450


def stats_header(im) -> bool:
    """Đầu đề bảng Half Time / Time Up: hộp xanh đậm (≈3,45,56) ở y=97 và dải xám
    "Stats" (170,170,170) ở y=162. Màn chọn đội / Kickoff cũng có nút A Confirm nhưng
    không có hai dải này (đo 17/11)."""
    teal = sum(
        1 for x in range(500, 790, 5)
        if (lambda p: p[0] < 20 and 35 < p[1] < 80 and 45 < p[2] < 95)(im.getpixel((x, 97)))
    )
    grey = sum(
        1 for x in range(560, 720, 4)
        if (lambda p: 150 < p[0] < 190 and abs(p[0] - p[1]) < 8 and abs(p[1] - p[2]) < 8)(im.getpixel((x, 162)))
    )
    return teal > 20 and grey > 10


def classify(im) -> str:
    """Chỉ bấm ở màn đã đo (17/11, Claude).

    lineup     màn đội hình / Game Plan
    result     menu đáy có logo E vàng: hết hiệp, hết trận, hoặc Pause Menu
    stats      bảng Half Time / Time Up (nút A Confirm góc trái dưới)
    replay_ctl sân + thanh tối dưới đáy: bảng Replay Control (do bấm A lúc đang đá)
    deadball   sân + bảng hướng dẫn góc phải: chờ người phát bóng/ném biên
    setpiece   sân + menu đỏ Select Kicker
    hud        sân đang đá
    other      màn tối / tải / video (trước đây gọi "replay" và bấm Start — sai: cả
               menu và bảng số cũng tối)
    """
    green = green_pitch(im)
    if mid_white(im) > 8000 and green < 5500:
        return "lineup"
    if yellow_e(im) > 1500 and white_bar(im) > 800:
        return "result"
    # Bảng số phủ sân đêm hoặc sân ngày (green 0..51 đã đo). Xét trước "tối".
    if confirm_icon(im) > 20 and green < 12000 and stats_header(im):
        return "stats"
    if green > 4000:
        if replay_bar(im) > 3000:
            return "replay_ctl"
        if help_guide(im):
            return "deadball"
        if score.looks_like_hud(im) and score_red(im) > 700:
            return "setpiece"
        return "hud"
    if start_match_bar(im) and green < 400:
        return "rematch"
    return "other"


def score_red(im) -> int:
    n = 0
    for y in range(90, 120, 2):
        for x in range(860, 1180, 3):
            r, g, b = im.getpixel((x, y))[:3]
            if r > 140 and g < 80 and b < 90:
                n += 1
    return n


def load_phase() -> str:
    try:
        phase = PHASE_PATH.read_text().strip()
    except OSError:
        return "play"
    return phase if phase in ("play", "half", "play2", "full") else "play"


def save_phase(phase: str) -> None:
    PHASE_PATH.parent.mkdir(exist_ok=True)
    PHASE_PATH.write_text(phase)


def pad(keys: str):
    subprocess.run([str(PAD), keys], check=False, capture_output=True, text=True)


def emit(fh, obj):
    line = json.dumps(obj, ensure_ascii=False)
    print(line, flush=True)
    fh.write(line + "\n")
    fh.flush()


def league():
    sys.path.insert(0, str(Path.home() / "pes6" / "pes17-web" / "league"))
    import store
    return store


def record_final(im):
    """Ghi tỷ số menu hết trận vào sổ, kèm ảnh riêng (loop_live.png bị ghi đè).
    OCR không chắc → KHÔNG ghi sổ, trả ok=False để vòng dừng chờ người đọc ảnh."""
    store = league()
    keep = SPIKE / "screens" / f"ft_{time.strftime('%m%d_%H%M%S')}.png"
    im.save(keep)
    r = score.read_pair(im, score.BIG_HOME, score.BIG_AWAY, "big")
    hg = r.get("home") if r and r.get("ok") else None
    ag = r.get("away") if r and r.get("ok") else None
    if hg is None or ag is None:
        return {"ok": False, "why": "OCR tỷ số hỏng", "ocr": r, "shot": keep.name}
    done = store.finish_playing(hg, ag, str(keep))
    return {"ok": True, "id": done["id"], "clubs": done["clubs"], "home": hg, "away": ag,
            "next": done["next"], "shot": keep.name}


def main():
    """Trận COM–COM (Coach Mode, pick.py bật). Không bấm gì khi đang ở sân, trừ:
    deadball → A, setpiece kẹt → B, replay_ctl → B (đóng bảng; chưa thử, có log)."""
    LOG.parent.mkdir(exist_ok=True)
    seen = None
    setpiece_n = 0
    last_kick = 0.0
    last_stats = 0.0
    last_dead = 0.0
    last_lineup = 0.0
    ctl_since = 0.0
    matches = 0
    claimed = None
    claim_live = 0
    # play → stats (hết hiệp 1) → result (vào hiệp 2) → play2 → stats → result (hết trận, chọn đội)
    phase = load_phase()
    with LOG.open("a") as fh:
        emit(fh, {"event": "loop_start", "phase": phase})
        while True:
            try:
                im = grab()
            except Exception as e:
                emit(fh, {"event": "error", "what": str(e)})
                time.sleep(3)
                continue
            screen = classify(im)
            now = time.monotonic()
            if screen != seen:
                emit(fh, {"event": "screen", "screen": screen, "was": seen, "phase": phase})
                if screen == "stats" and phase in ("play", "play2"):
                    phase = "half" if phase == "play" else "full"
                    pad("A w:0.8")
                    last_stats = now
                    claimed = phase
                    claim_live = 0
                    save_phase(phase)
                    emit(fh, {"event": "confirm", "screen": "stats", "phase": phase})
                elif screen == "result" and phase == "half":
                    phase = "play2"
                    claimed = None
                    save_phase(phase)
                    pad("A w:0.8")  # ô 2nd Half chọn sẵn
                    emit(fh, {"event": "confirm", "screen": "result", "phase": phase})
                elif screen == "result" and phase == "full":
                    try:
                        final = record_final(im)
                    except Exception as e:
                        final = {"ok": False, "error": str(e)}
                    claimed = None
                    emit(fh, {"event": "end", **final})
                    if not final.get("ok"):
                        emit(fh, {"event": "halt", "why": "chưa ghi được tỷ số"})
                        return
                    nxt = final.get("next")
                    if not nxt:
                        save_phase("play")
                        emit(fh, {"event": "season_done"})
                        return
                    import pick
                    picked = pick.choose(nxt[0], nxt[1])
                    emit(fh, {"event": "pick", **picked})
                    if not picked.get("ok"):
                        # Dừng hẳn: đang giữa menu chọn đội, bấm tiếp dễ Rematch nhầm cặp.
                        emit(fh, {"event": "halt", "why": "pick failed"})
                        return
                    matches += 1
                    phase = "play"
                    save_phase(phase)
                    emit(fh, {"event": "match_start", "n": matches})
                elif screen == "result":
                    # Pause Menu trông giống menu hết hiệp. A mở Game Plan. B trở lại sân.
                    pad("B w:0.6")
                    emit(fh, {"event": "resume", "phase": phase})
                elif screen == "lineup":
                    last_lineup = now
                    keep = SPIKE / "screens" / f"lineup_{time.strftime('%H%M%S')}.png"
                    im.save(keep)
                    pad("B w:0.6")
                    emit(fh, {"event": "lineup_back", "shot": keep.name})
                elif screen == "rematch":
                    # Giải không dùng Rematch; thẻ đội hình trước trận cũng khớp mẫu này
                    # (17/11) → chỉ ghi log, không bấm.
                    emit(fh, {"event": "rematch_seen"})
                seen = screen
            elif screen == "stats" and phase in ("half", "full") and now - last_stats > 4:
                pad("A w:0.8")
                last_stats = now
                emit(fh, {"event": "confirm", "screen": "stats", "phase": phase, "retry": True})
            elif screen == "lineup" and now - last_lineup > 2:
                last_lineup = now
                pad("B w:0.6")
                emit(fh, {"event": "lineup_back", "retry": True})
            if screen == "deadball" and now - last_dead > 2.5:
                pad("A w:0.3")
                last_dead = now
                emit(fh, {"event": "deadball_a", "phase": phase})
            if screen == "replay_ctl":
                ctl_since = ctl_since or now
                if now - ctl_since > 4:
                    pad("B w:0.6")
                    ctl_since = now
                    emit(fh, {"event": "replay_ctl_b"})
            else:
                ctl_since = 0.0
            if claimed and screen in ("hud", "deadball"):
                # Nhận nhầm hết hiệp mà sân vẫn đá → trả phase.
                claim_live += 1
                if claim_live >= 2:
                    phase = "play" if claimed == "half" else "play2"
                    claimed = None
                    save_phase(phase)
                    emit(fh, {"event": "revert", "phase": phase})
            elif screen == "result":
                claimed = None
            if screen == "setpiece":
                setpiece_n += 1
                if setpiece_n >= 2 and now - last_kick > 8:
                    pad("B w:0.5 B")
                    last_kick = now
                    setpiece_n = 0
                    emit(fh, {"event": "unstick"})
            else:
                setpiece_n = 0
            time.sleep(1.5)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
