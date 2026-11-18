#!/usr/bin/env python3
"""Đọc tỷ số PES17 từ ảnh (HUD trong trận hoặc màn Stats/Half Time).

  score.py [ảnh.png]     — mặc định chụp mới qua shot.sh
  score.py --watch [N]   — poll, emit JSON khi đổi tỷ số

OCR màn hình (không mem). Venv: spike-league/.venv (Pillow).
"""
from __future__ import annotations

import glob, json, subprocess, sys, time
from pathlib import Path

SPIKE = Path(__file__).resolve().parents[1]
for p in glob.glob(str(SPIKE / ".venv" / "lib" / "python*" / "site-packages")):
    sys.path.insert(0, p)
from PIL import Image

DIG = SPIKE / "digits"
SHOT = SPIKE / "tools" / "shot.sh"
SIZE = (16, 22)
THR = 185

# Ô cố định (calibrated 17/11 trên 1280×720 root shot)
HUD_HOME = (95, 74, 107, 94)
HUD_AWAY = (154, 74, 166, 94)
HUD_CLOCK = (300, 72, 345, 96)  # MM:SS digits area (best-effort)

STATS_HOME = (448, 238, 480, 272)
STATS_AWAY = (800, 238, 832, 272)
# Số to trên menu 2nd Half / Rematch (1280×720)
BIG_HOME = (500, 105, 570, 170)
BIG_AWAY = (710, 105, 780, 170)


def cell(im: Image.Image, box, thr=THR, size=SIZE) -> Image.Image:
    """Crop → threshold → pad-center → resize (không trim chặt để NCC ổn)."""
    x0, y0, x1, y1 = box
    gray = im.crop((x0, y0, x1, y1)).convert("L")
    bw = gray.point(lambda p: 255 if p >= thr else 0)
    # optional light trim with margin
    pix = bw.load()
    w, h = bw.size
    xs = [x for y in range(h) for x in range(w) if pix[x, y] > 0]
    ys = [y for y in range(h) for x in range(w) if pix[x, y] > 0]
    if xs:
        m = 1
        bw = bw.crop(
            (
                max(0, min(xs) - m),
                max(0, min(ys) - m),
                min(w, max(xs) + 1 + m),
                min(h, max(ys) + 1 + m),
            )
        )
    # pad to square-ish then resize
    w, h = bw.size
    side = max(w, h, 8)
    canvas = Image.new("L", (side, side), 0)
    canvas.paste(bw, ((side - w) // 2, (side - h) // 2))
    return canvas.resize(size, Image.Resampling.NEAREST)


def ncc(a: Image.Image, b: Image.Image) -> float:
    pa, pb = list(a.getdata()), list(b.getdata())
    if len(pa) != len(pb):
        return -1.0
    ma, mb = sum(pa) / len(pa), sum(pb) / len(pb)
    num = sum((x - ma) * (y - mb) for x, y in zip(pa, pb))
    da = sum((x - ma) ** 2 for x in pa) ** 0.5
    db = sum((y - mb) ** 2 for y in pb) ** 0.5
    if da * db == 0:
        return 0.0
    return num / (da * db)


def load_templates(kind: str | None = None):
    """Load digits/{n}.png and digits/{n}_{kind}.png variants."""
    tpls: dict[int, list[Image.Image]] = {}
    if not DIG.exists():
        return tpls
    for p in DIG.glob("*.png"):
        stem = p.stem
        if stem.isdigit():
            d = int(stem)
        elif "_" in stem and stem.split("_", 1)[0].isdigit():
            d = int(stem.split("_", 1)[0])
            if kind and not stem.endswith("_" + kind) and stem != str(d):
                # keep generic + matching kind
                if kind not in stem:
                    continue
        else:
            continue
        img = Image.open(p).convert("L")
        if img.size != SIZE:
            # re-normalize via pad path
            canvas = Image.new("L", SIZE, 0)
            img = img.resize(SIZE, Image.Resampling.NEAREST)
        tpls.setdefault(d, []).append(img)
    return tpls


def match_digit(img: Image.Image, tpls: dict[int, list[Image.Image]], min_score=0.55):
    best, bestv = None, -1.0
    for d, variants in tpls.items():
        for t in variants:
            v = ncc(img, t)
            if v > bestv:
                best, bestv = d, v
    if bestv < min_score:
        return None, bestv
    return best, bestv


def ink_ratio(im, box, thr=THR) -> float:
    c = im.crop(box).convert("L")
    pix = list(c.getdata())
    if not pix:
        return 0.0
    return sum(1 for p in pix if p >= thr) / len(pix)


def looks_like_stats(im) -> bool:
    teal = sum(
        1
        for y in range(60, 110)
        for x in range(10, 200)
        if (lambda p: 20 < p[0] < 100 and p[1] > p[0] + 10 and p[2] > p[0] + 10)(im.getpixel((x, y)))
    )
    # panel center near "Goals Scored"
    mid = sum(1 for x in range(560, 720) for y in range(240, 265) if sum(im.getpixel((x, y))[:3]) > 480)
    return teal < 2500 and mid > 20


def looks_like_hud(im) -> bool:
    teal = sum(
        1
        for y in range(60, 110)
        for x in range(10, 200)
        if (lambda p: 20 < p[0] < 100 and p[1] > p[0] + 10 and p[2] > p[0] + 10)(im.getpixel((x, y)))
    )
    return teal > 2500


def read_pair(im, home_box, away_box, kind: str):
    tpls = load_templates(kind)
    # also load generic
    for d, vs in load_templates(None).items():
        tpls.setdefault(d, []).extend(vs)
    if not tpls:
        return None
    # skip empty cells (no scoreboard)
    if ink_ratio(im, home_box) < 0.02 or ink_ratio(im, away_box) < 0.02:
        return None
    hg, ag = cell(im, home_box), cell(im, away_box)
    hd, hs = match_digit(hg, tpls)
    ad, as_ = match_digit(ag, tpls)
    if hd is None or ad is None:
        return {
            "ok": False,
            "reason": "digit_mismatch",
            "home_score": round(hs, 3),
            "away_score": round(as_, 3),
            "source": kind,
        }
    return {
        "ok": True,
        "home": hd,
        "away": ad,
        "source": kind,
        "confidence": "ocr",
        "match_scores": {"home": round(hs, 3), "away": round(as_, 3)},
    }


def grab(path: Path | None = None) -> tuple[Image.Image, Path]:
    if path is None:
        name = f"score_{time.strftime('%H%M%S', time.gmtime())}"
        subprocess.run([str(SHOT), name], check=True, capture_output=True)
        path = SPIKE / "screens" / f"{name}.png"
    return Image.open(path).convert("RGB"), path


def read_score(path: Path | None = None) -> dict:
    im, used = grab(path)
    out = {"image": str(used), "templates": sorted(load_templates().keys())}
    if looks_like_stats(im):
        out["screen"] = "stats"
        r = read_pair(im, STATS_HOME, STATS_AWAY, "stats")
    elif looks_like_hud(im):
        out["screen"] = "hud"
        r = read_pair(im, HUD_HOME, HUD_AWAY, "hud")
    else:
        out["screen"] = "unknown"
        r = read_pair(im, STATS_HOME, STATS_AWAY, "stats")
        if not r or not r.get("ok"):
            r = read_pair(im, HUD_HOME, HUD_AWAY, "hud")
    if not r:
        out.update({"ok": False, "reason": "no_scoreboard"})
    else:
        out.update(r)
    return out


def save_template(d: int, im: Image.Image, box, name: str):
    DIG.mkdir(exist_ok=True)
    g = cell(im, box)
    path = DIG / f"{name}.png"
    g.save(path)
    return path


def build_templates():
    saved = []
    hud_p = SPIKE / "screens" / "score_now.png"
    ht_p = SPIKE / "screens" / "state_check.png"
    if hud_p.exists():
        hud = Image.open(hud_p).convert("RGB")
        save_template(0, hud, HUD_HOME, "0_hud")
        save_template(0, hud, HUD_AWAY, "0_hud_away")
        save_template(0, hud, HUD_HOME, "0")  # default
        saved += [0]
    if ht_p.exists():
        ht = Image.open(ht_p).convert("RGB")
        save_template(0, ht, STATS_HOME, "0_stats")
        save_template(0, ht, STATS_AWAY, "0_stats_away")
        saved += [0]
    return sorted(set(saved))


def emit(obj):
    print(json.dumps(obj, ensure_ascii=False), flush=True)


def main(argv):
    if len(argv) >= 2 and argv[1] == "--build-templates":
        emit({"event": "templates", "saved": build_templates(), "dir": str(DIG)})
        return 0
    if len(argv) >= 2 and argv[1] == "--watch":
        n = int(argv[2]) if len(argv) > 2 else 0
        prev = None
        i = 0
        emit({"event": "watch_start"})
        while n == 0 or i < n:
            r = read_score(None)
            if r.get("ok"):
                key = (r["home"], r["away"])
                if prev is None:
                    emit({"event": "score", "home": r["home"], "away": r["away"], "source": r.get("source"), "screen": r.get("screen")})
                elif key != prev:
                    side = "home" if r["home"] > prev[0] else "away"
                    emit({"event": "goal", "side": side, "home": r["home"], "away": r["away"], "source": r.get("source")})
                prev = key
            else:
                emit({"event": "ocr_miss", **{k: r.get(k) for k in ("reason", "screen", "home_score", "away_score")}})
            i += 1
            time.sleep(1.2)
        emit({"event": "watch_end"})
        return 0

    path = Path(argv[1]) if len(argv) > 1 else None
    emit(read_score(path))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
