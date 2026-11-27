#!/usr/bin/env python3
"""Chọn giải/CLB ở màn Select Team của PES 2017 (Exhibition) bằng mẫu ảnh tên (game nhớ vị trí lần trước → không đếm phím).

Ô tên đang chọn (thẻ lớn) của mỗi bên: crop 470x50 tại y=270, x=120 (nhà) / 696 (khách); chữ trắng trên nền tối.
Mẫu: templates/leagues/<mã>.png, templates/teams/<mã đội PES17>.png (mặt nạ trắng/đen cùng cỡ).
Danh sách quay vòng → seek đi Down tối đa N bước; chạm mép (tên không đổi) thì đổi chiều.

  m17nav.py header <ảnh> home|away            → mã giải/đội khớp nhất
  m17nav.py seek-league home|away <mã>        → đang ở danh sách giải: tìm giải
  m17nav.py learn home|away <tên> [n]         → đang ở danh sách đội: đi Down n bước, lưu mặt nạ learn/<tên>/NN.png + ghép ảnh xem
"""
from __future__ import annotations

import os, queue, subprocess, sys, threading, time
from contextlib import contextmanager
from pathlib import Path
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]
TPL = ROOT / "templates"
CLUBS = TPL / "clubs.json"
SCREENS = ROOT / "screens"
X = {"home": 120, "away": 696}
Y, W, H = 270, 470, 50


def shot(name: str = "nav") -> Image.Image:
    for i in range(3):   # xwd đôi khi lỗi thoáng qua
        if subprocess.run([str(ROOT / "tools" / "shot.sh"), name], capture_output=True).returncode == 0:
            break
        time.sleep(0.5)
    else:
        raise RuntimeError("không chụp được màn hình")
    return Image.open(SCREENS / f"{name}.png").convert("RGB")


SLOT = {"home": 0, "away": 0}   # tay cầm (index selkies) chọn đội từng bên; trận 2 người: away = 1 (P2 đứng cột AWAY)


BENCH = os.environ.get("PES17_BENCH", "pes17-bench")
_PADS: dict[int, tuple[subprocess.Popen, "queue.Queue[str]"]] = {}
_PERSIST = False


def pad(keys: str, slot: int = 0) -> None:
    """Gửi chuỗi nút. Trong session(): giữ 1 kết nối tay cầm ảo/slot (pad.sh mỗi lần phải nối lại + chờ cắm pad ~1,3 s)."""
    if _PERSIST and _pad_line(slot, keys):
        return
    subprocess.run([str(ROOT / "tools" / "pad.sh"), keys], check=True, capture_output=True,
                   env={**os.environ, "PAD_SLOT": str(slot)})


def _pad_line(slot: int, keys: str) -> bool:
    if slot not in _PADS:
        subprocess.run(["docker", "cp", str(ROOT / "tools" / "pad17.py"), f"{BENCH}:/tmp/pad17.py"], capture_output=True)
        p = subprocess.Popen(["docker", "exec", "-i", BENCH, "/lsiopy/bin/python3", "/tmp/pad17.py", str(slot)],
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
        q: "queue.Queue[str]" = queue.Queue()
        threading.Thread(target=lambda: [q.put(l.strip()) for l in p.stdout] and None, daemon=True).start()
        _PADS[slot] = (p, q)
        if not _wait_line(q, "ready", 15):
            _close(slot)
            return False
    p, q = _PADS[slot]
    try:
        p.stdin.write(keys + "\n")
        p.stdin.flush()
    except OSError:
        _close(slot)
        return False
    waits = sum(float(k[2:]) for k in keys.split() if k.startswith("w:"))
    if _wait_line(q, "ok", waits + 0.5 * len(keys.split()) + 10):
        return True
    _close(slot)   # treo/đứt → bỏ kết nối; lần sau nối lại (phím có thể đã gửi một phần — người gọi vẫn dò lại màn hình)
    return True


def _wait_line(q: "queue.Queue[str]", want: str, timeout: float) -> bool:
    end = time.monotonic() + timeout
    while (left := end - time.monotonic()) > 0:
        try:
            if q.get(timeout=left) == want:
                return True
        except queue.Empty:
            break
    return False


def _close(slot: int) -> None:
    p, _ = _PADS.pop(slot)
    try:
        p.stdin.close()
        p.wait(3)
    except Exception:
        p.kill()


@contextmanager
def session():
    """Đi menu nhanh: giữ kết nối tay cầm suốt khối lệnh, đóng khi ra (trước khi người chơi cầm tay cầm)."""
    global _PERSIST
    _PERSIST = True
    try:
        yield
    finally:
        _PERSIST = False
        for slot in list(_PADS):
            _close(slot)


def crop(im: Image.Image, side: str) -> Image.Image:
    x = X[side]
    return im.crop((x, Y, x + W, Y + H))


def mask(img: Image.Image) -> Image.Image:
    """Chữ trắng (>185) → 255. Nhận cả ảnh đã crop 470x50 lẫn mẫu cắt từ ảnh ghép."""
    return img.convert("L").point(lambda v: 255 if v > 185 else 0)


def _iou1(a: Image.Image, b: Image.Image) -> float:
    a1, b1 = a.convert("1"), b.convert("1")
    union = ImageChops.logical_or(a1, b1).convert("L").histogram()[255]
    return ImageChops.logical_and(a1, b1).convert("L").histogram()[255] / union if union else 0.0


def iou(a: Image.Image, b: Image.Image, shift: int = 4) -> float:
    """IoU điểm trắng, cho lệch ngang ±shift px (chữ bên khách lệch ~1px so với mẫu chụp bên nhà)."""
    if shift == 0:
        return _iou1(a, b)
    return max(_iou1(a, ImageChops.offset(b, dx, 0)) for dx in range(-shift, shift + 1))


_TPL_CACHE: dict[str, tuple[tuple, dict]] = {}


def templates(kind: str) -> dict[str, Image.Image]:
    """Mặt nạ mẫu (nhớ đệm theo danh sách file + mtime: thêm/sửa mẫu là tự nạp lại)."""
    files = sorted((TPL / kind).glob("*.png"))
    key = tuple((p.name, p.stat().st_mtime_ns) for p in files)
    hit = _TPL_CACHE.get(kind)
    if hit is None or hit[0] != key:
        hit = (key, {p.stem: mask(Image.open(p)) for p in files})
        _TPL_CACHE[kind] = hit
    return dict(hit[1])


def best(m: Image.Image, tpls: dict[str, Image.Image]) -> tuple[str | None, float]:
    if len(tpls) > 24:   # nhiều mẫu (403 đội): lọc nhanh không dịch ngang, chỉ so kỹ 8 mẫu gần nhất (~2 s → ~0,3 s)
        near = sorted(tpls, key=lambda k: _iou1(m, tpls[k]), reverse=True)[:8]
        tpls = {k: tpls[k] for k in near}
    top, score = None, 0.0
    for k, t in tpls.items():
        s = iou(m, t)
        if s > score:
            top, score = k, s
    return top, score


LEAGUE_ORDER = ["eng1", "eng2", "fra1", "fra2", "ita1", "ita2", "ned1", "esp1", "esp2", "por1", "peu", "other_eu",
                "bra", "arg", "chi", "pla", "other_la"]   # thứ tự trong game (đo 18/11)


def _team_order() -> list[str]:
    import json
    p = TPL / "clubs.json"
    if not p.exists():
        return []
    d = json.loads(p.read_text())
    return sorted(d, key=lambda k: (LEAGUE_ORDER.index(d[k]["league"]), d[k]["pos"]))


ORDER = {"leagues": LEAGUE_ORDER, "teams": _team_order()}


# Ngưỡng khớp mẫu thẻ tên (mẫu chụp ở 720p). Màn 1080p co về 720p (PO 26/11): đúng mẫu còn 0,81–0,86 (720p ~0,85),
# mẫu sai gần nhất ≤ 0,45 (Ligue 1/2 ~0,82 → vẫn bắt buộc là mẫu khớp nhất) → 0,75.
MATCH_OK = 0.75


def seek(side: str, kind: str, target: str, max_steps: int = 60, step_wait: float = 0.7, ok: float = MATCH_OK) -> dict:
    tpls = templates(kind)
    if target not in tpls:
        return {"ok": False, "why": f"thiếu mẫu {kind}/{target}"}
    if kind == "teams":   # danh sách trong game chỉ có đội cùng giải → chỉ so mẫu giải đó (403 mẫu: ~2 s/lần so, 1 giải ~0,1 s)
        import json
        d = json.loads(CLUBS.read_text())
        lg = d.get(target, {}).get("league")
        tpls = {k: v for k, v in tpls.items() if d.get(k, {}).get("league") == lg} or tpls
    seen, prev = [], None
    keys = ("Down", "Up")
    cur, cur_s = best(mask(crop(shot(f"seek_{side}"), side)), tpls)
    order = ORDER.get(kind) or []
    if cur in order and target in order and cur_s >= ok and order.index(target) < order.index(cur):
        keys = ("Up", "Down")   # biết thứ tự danh sách → đi đúng chiều trước
    if cur in order and target in order and cur_s >= ok:
        jump = abs(order.index(target) - order.index(cur)) - 1
        if jump >= 2:   # nhảy nhanh gần tới đích (phím có thể bị nuốt → vẫn dò lại từng bước)
            pad(" ".join([f"{keys[0]} w:0.3"] * jump) + " w:0.5", SLOT[side])
    for key in keys:
        for _ in range(max_steps):
            m = mask(crop(shot(f"seek_{side}"), side))
            s = iou(m, tpls[target])
            seen.append(round(s, 2))
            if s >= ok and best(m, tpls)[0] == target:   # Ligue 1/Ligue 2 giống nhau 0.82 → phải là mẫu khớp nhất
                return {"ok": True, "steps": len(seen) - 1, "score": round(s, 2)}
            if prev is not None and iou(m, prev, 0) > 0.97:
                break   # chạm mép (danh sách không quay vòng) → đổi chiều
            prev = m
            pad(f"{key} w:{step_wait}", SLOT[side])
        prev = None
    return {"ok": False, "why": f"không thấy {kind}/{target} bên {side}", "seen": seen[-8:]}


def to_edge(side: str, key: str, step_wait: float = 0.6, max_steps: int = 80) -> int:
    prev = None
    for i in range(max_steps):
        m = mask(crop(shot(f"edge_{side}"), side))
        if prev is not None and iou(m, prev, 0) > 0.97:
            return i
        prev = m
        pad(f"{key} w:{step_wait}", SLOT[side])
    return max_steps


def learn(side: str, name: str, step_wait: float = 0.7, max_steps: int = 60) -> None:
    """Đang ở danh sách đội: lên đầu, rồi đi Down tới cuối; lưu mặt nạ theo thứ tự trong game + ảnh ghép để đối chiếu tên."""
    to_edge(side, "Up")
    out = ROOT / "learn" / name
    out.mkdir(parents=True, exist_ok=True)
    crops, prev = [], None
    for i in range(max_steps):
        c = crop(shot(f"learn_{side}"), side)
        m = mask(c)
        if prev is not None and iou(m, prev, 0) > 0.97:
            break
        m.save(out / f"{i:02d}.png")
        crops.append(c)
        prev = m
        pad(f"Down w:{step_wait}")
    sheet = Image.new("RGB", (W + 40, H * len(crops)), "black")
    for i, c in enumerate(crops):
        sheet.paste(c, (40, i * H))
    sheet.save(out / "sheet.png")
    print(len(crops), out / "sheet.png")




def pick_side(side: str, club: int) -> dict:
    """Đang ở danh sách giải bên `side` (sau B hoặc vừa chốt đội nhà): chọn giải rồi CLB, bấm A chốt."""
    import json
    info = json.loads(CLUBS.read_text()).get(str(club))
    if info is None:
        return {"ok": False, "why": f"CLB {club} chưa có mẫu (templates/clubs.json)"}
    r = seek(side, "leagues", info["league"], step_wait=0.6)
    if not r["ok"]:
        return r
    pad("A w:2.5", SLOT[side])
    r = seek(side, "teams", str(club), step_wait=0.6)
    if not r["ok"]:
        return r
    pad("A w:2", SLOT[side])
    return {"ok": True}


if __name__ == "__main__":
    a = sys.argv[1:]
    if a[0] == "header":
        im = Image.open(a[1]).convert("RGB")
        m = mask(crop(im, a[2]))
        print(best(m, templates("leagues")), best(m, templates("teams")))
    elif a[0] == "seek-league":
        print(seek(a[1], "leagues", a[2]))
    elif a[0] == "pick-side":   # pick-side home|away CLB [slot]
        if len(a) > 3:
            SLOT[a[1]] = int(a[3])
        print(pick_side(a[1], int(a[2])))
    elif a[0] == "learn":
        learn(a[1], a[2])
