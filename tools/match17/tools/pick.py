#!/usr/bin/env python3
"""Từ menu hết trận (ô Rematch đang chọn) sang cặp trong lịch — chọn đội bằng mắt.

Danh sách đội NHỚ lần chọn trước (17/11: đội khách mở ra ở Bournemouth chứ không
phải Arsenal) nên không đếm Down. Mỗi bước Down chụp tên trên thẻ lớn, so với mẫu
`teams/<ID>.png` (cắt 300x44 tại y=272; x=130 bên nhà, x=706 bên khách).

Đường đi (thử tay 17/11, AVL–BRE):
  Rematch → Right (Select Team) → A → [tải] → English League (nhà) A
  → Down… đến đội nhà → A → English League (khách) A → Down… → A
  → màn Kickoff: Y bật Coach Mode (biểu tượng HLV xanh trên danh sách đội nhà;
     máy đá cả 2 bên, bóng chết không đứng chờ) → A (Kickoff).
"""
from __future__ import annotations

import subprocess, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import loop

SPIKE = loop.SPIKE
TEAMS = SPIKE / "teams"
SHOT = loop.SHOT
Image = loop.score.Image
NAME_Y = 272
NAME_X = {"home": 130, "away": 706}
CLUBS = ("ARS", "AVL", "BOU", "BRE")
MAX_DOWN = 30


def shot(name: str):
    subprocess.run([str(SHOT), name], check=False, capture_output=True)
    return Image.open(SPIKE / "screens" / f"{name}.png").convert("RGB")


def name_mask(im, side: str):
    x = NAME_X[side]
    return im.convert("L").crop((x, NAME_Y, x + 300, NAME_Y + 44)).point(lambda v: 255 if v > 185 else 0)


def similarity(a, b) -> float:
    """IoU điểm trắng của hai mặt nạ tên."""
    inter = union = 0
    for u, v in zip(a.tobytes(), b.tobytes()):
        if u or v:
            union += 1
            if u and v:
                inter += 1
    return inter / union if union else 0.0


def which_team(im, side: str) -> tuple[str | None, float]:
    m = name_mask(im, side)
    best, score = None, 0.0
    for cid in CLUBS:
        p = TEAMS / f"{cid}.png"
        if p.exists():
            s = similarity(m, Image.open(p).convert("L"))
            if s > score:
                best, score = cid, s
    return (best, score) if score >= 0.75 else (None, score)


def seek(side: str, target: str) -> dict:
    """Đang ở danh sách đội bên `side`: tìm `target`. Danh sách không quay vòng và
    mở ở đội chọn lần trước → đi Up đến khi thấy hoặc chạm đầu (tên không đổi),
    rồi đi Down."""
    seen = []
    prev = None
    for key in ("Up", "Down"):
        for _ in range(MAX_DOWN):
            im = shot(f"pick_{side}")
            cid, s = which_team(im, side)
            seen.append(f"{cid}:{s:.2f}")
            if cid == target:
                return {"ok": True, "steps": len(seen) - 1}
            m = name_mask(im, side)
            if prev is not None and similarity(m, prev) > 0.95:
                break  # chạm mép danh sách
            prev = m
            loop.pad(f"{key} w:0.6")
        prev = None
    return {"ok": False, "why": f"không thấy {target} bên {side}", "seen": seen[-6:]}


def coach_icon(im) -> int:
    """Biểu tượng HLV xanh trên danh sách đội nhà (màn Kickoff). Đo: tắt 0, bật 243."""
    n = 0
    for y in range(240, 360, 2):
        for x in range(330, 460, 2):
            r, g, b = im.getpixel((x, y))
            if b > 180 and r < 90 and 90 < g < 190:
                n += 1
    return n


def is_ask(im) -> bool:
    """Hộp 'Return to Team Select?' — nền trắng lớn."""
    return loop.mid_white(im) > 8000 and loop.green_pitch(im) < 500


def wait_bright(tag: str, timeout: float = 40) -> bool:
    """Chờ hết màn đen tải."""
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        im = shot(tag).convert("L").resize((64, 36))
        if sum(im.getdata()) / (64 * 36) > 25:
            return True
        time.sleep(2)
    return False


def choose(home_id: str, away_id: str) -> dict:
    out = {"home": home_id, "away": away_id}
    if home_id not in CLUBS or away_id not in CLUBS:
        return {"ok": False, "why": "unknown club", **out}
    loop.pad("Right w:0.6 A w:2.5")
    im = shot("pick_enter")
    if loop.classify(im) == "result":
        return {"ok": False, "why": "still on result", **out}
    if is_ask(im):
        loop.pad("Left w:0.4 A w:2.5")
    if not wait_bright("pick_enter"):
        return {"ok": False, "why": "kẹt màn tải", **out}
    time.sleep(1.5)
    loop.pad("A w:1.5")  # vào English League bên nhà
    return from_home_list(home_id, away_id)


def from_home_list(home_id: str, away_id: str) -> dict:
    """Tiếp từ lúc đang ở danh sách đội bên nhà."""
    out = {"home": home_id, "away": away_id}
    r = seek("home", home_id)
    if not r["ok"]:
        return {**r, **out}
    loop.pad("A w:1.5 A w:1.5")  # chốt đội nhà, vào English League bên khách
    r = seek("away", away_id)
    if not r["ok"]:
        return {**r, **out}
    loop.pad("A w:4")  # chốt khách → màn Kickoff (chờ tải mẫu cầu thủ)
    coach = 0
    for _ in range(3):
        loop.pad("Y w:1.5")
        coach = coach_icon(shot("pick_coach"))
        if coach > 100:
            break
    if coach <= 100:
        return {"ok": False, "why": "không bật được Coach Mode", "coach": coach, **out}
    loop.pad("A w:1.0")  # Kickoff
    shot("pick_kick")
    sys.path.insert(0, str(Path.home() / "pes6" / "pes17-web" / "league"))
    import store
    store.mark_playing(home_id, away_id)
    return {"ok": True, "coach": coach, **out}


if __name__ == "__main__":
    # pick.py from-home ARS BRE   — đang ở danh sách đội nhà, chọn tiếp
    # pick.py <ảnh> home|away     — thử nhận đội trên ảnh
    if sys.argv[1] == "from-home":
        print(from_home_list(sys.argv[2], sys.argv[3]))
    else:
        print(which_team(Image.open(sys.argv[1]).convert("RGB"), sys.argv[2]))
