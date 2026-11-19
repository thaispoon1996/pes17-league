#!/usr/bin/env python3
"""Đọc chữ trên màn PES 2017 bằng RapidOCR (onnxruntime, CPU, 1 luồng để không tranh CPU với game).

Toạ độ đo 18/11 trên 1280x720 (giao diện HANO V6):
  HUD đang đá:      tỷ số "1-0" (130,68)-(215,98), đồng hồ "51:29" (285,68)-(375,98)   — chỉ nhận dạng (không dò chữ), ~30 ms
  menu nghỉ/hết trận: tiêu đề "Half Time"/"Full Time" (480,60)-(800,102), số to + đồng hồ giữa (480,95)-(800,180)
  bảng số Half Time/Time Up: hàng "Goals Scored" (380,235)-(900,270)
"""
from __future__ import annotations

import os, re, sys
os.environ.setdefault("OMP_NUM_THREADS", "1")

import numpy as np
from PIL import Image

_OCR = None

HUD_SCORE = (130, 68, 215, 98)
HUD_CLOCK = (285, 68, 375, 98)
MENU_TITLE = (480, 60, 800, 102)
MENU_SCORE = (480, 95, 800, 180)
STATS_GOALS = (380, 235, 900, 270)


def ocr():
    global _OCR
    if _OCR is None:
        from rapidocr_onnxruntime import RapidOCR
        _OCR = RapidOCR(intra_op_num_threads=1, inter_op_num_threads=1)
    return _OCR


def rec(im: Image.Image, box) -> tuple[str, float]:
    r, _ = ocr()(np.array(im.convert("RGB").crop(box)), use_det=False, use_cls=False)
    return (r[0][0], float(r[0][1])) if r else ("", 0.0)


def det(im: Image.Image, box) -> list[tuple[str, float, float]]:
    """[(chữ, điểm, tâm x tuyệt đối)] theo thứ tự trái → phải."""
    r, _ = ocr()(np.array(im.convert("RGB").crop(box)))
    out = [(t, float(s), box[0] + sum(p[0] for p in pts) / 4) for pts, t, s in (r or [])]
    return sorted(out, key=lambda x: x[2])


def hud(im: Image.Image) -> dict | None:
    """Đang đá: {"home","away","minute"} (minute = phút đồng hồ game, None nếu không đọc được). Không phải HUD → None."""
    t, s = rec(im, HUD_SCORE)
    m = re.fullmatch(r"(\d{1,2})\s*[-–]\s*(\d{1,2})", t.strip())
    if not m or s < 0.8:
        return None
    c, cs = rec(im, HUD_CLOCK)
    mm = re.fullmatch(r"(\d{1,3}):(\d{2})", c.strip())
    return {"home": int(m[1]), "away": int(m[2]), "minute": int(mm[1]) if mm and cs > 0.7 else None}


def menu(im: Image.Image) -> dict | None:
    """Menu nghỉ giữa hiệp / hết trận: {"title","home","away","clock"}; không đọc được 2 số → None."""
    title = rec(im, MENU_TITLE)[0]
    nums = [(t, x) for t, s, x in det(im, MENU_SCORE) if s > 0.6]
    digits = [int(t) for t, x in nums if re.fullmatch(r"\d{1,2}", t)]
    clock = next((t for t, _ in nums if re.fullmatch(r"\d{1,3}:\d{2}", t)), None)
    if len(digits) != 2:
        return None
    return {"title": title, "home": digits[0], "away": digits[1], "clock": clock}


def stats_goals(im: Image.Image) -> tuple[int, int] | None:
    toks = [t for t, s, _ in det(im, STATS_GOALS) if s > 0.3]
    nums = [int(t) for t in toks if re.fullmatch(r"\d{1,2}", t)]
    return (nums[0], nums[-1]) if len(nums) == 2 and any("oal" in t for t in toks) else None


def stats_screen(im: Image.Image) -> bool:
    """Bảng số Half Time / Time Up: có dòng "Goals Scored", kể cả khi không đọc được số (19/11: số 0 bên nhà hay bị sót → kẹt nghỉ giữa hiệp)."""
    return any("oal" in t and s > 0.5 for t, s, _ in det(im, STATS_GOALS))


RANK_ROW0, RANK_STEP = 262, 25                                        # tâm y hàng đầu, khoảng cách hàng
RANK_COLS = {"home": ((286, 326), (550, 592)), "away": ((672, 712), (932, 974))}   # (x số áo, x điểm)


def _rec_big(im: Image.Image, box, scale: int = 3) -> tuple[str, float]:
    c = im.convert("RGB").crop(box)
    c = c.resize((c.width * scale, c.height * scale), Image.LANCZOS)
    r, _ = ocr()(np.array(c), use_det=False, use_cls=False)
    return (r[0][0].strip(), float(r[0][1])) if r else ("", 0.0)


def rankings(im: Image.Image) -> dict[str, list[dict]]:
    """Menu hết trận, bảng Player Rankings (11 người đầu bảng = đá chính): {"home": [{"shirt","rating"}...], "away": [...]}.
    Đọc từng ô (phóng 3x): số áo ở cột trái, điểm "6.5" ở cột phải; ô đọc không chắc thì bỏ dòng."""
    out = {}
    for side, ((sx0, sx1), (rx0, rx1)) in RANK_COLS.items():
        lst = []
        for i in range(11):
            y = RANK_ROW0 + RANK_STEP * i
            st, ss = _rec_big(im, (sx0, y - 12, sx1, y + 12))
            rt, rs = _rec_big(im, (rx0, y - 12, rx1, y + 12))
            if re.fullmatch(r"\d{1,2}", st) and ss > 0.8 and re.fullmatch(r"\d{1,2}\.\d", rt) and rs > 0.8:
                lst.append({"shirt": int(st), "rating": float(rt)})
        out[side] = lst
    return out


if __name__ == "__main__":
    im = Image.open(sys.argv[1]).convert("RGB")
    print({"hud": hud(im), "menu": menu(im), "stats": stats_goals(im)})
    if len(sys.argv) > 2:
        print(rankings(im))
