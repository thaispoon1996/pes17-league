#!/usr/bin/env python3
"""Màu chủ đạo áo đấu (PO 23/11: chọn áo trước trận, cảnh báo trùng màu, đội BOT tự đổi áo thứ ba khi trùng).

  kitcolors.py <$DATA>   → $DATA/kits/colors.json {"{ClubId:N}-{1st|2nd|3rd|gk}": "#rrggbb"} (KitStore đọc)

Màu = trung bình điểm ảnh không trong suốt ở vùng thân áo (giữa ảnh, bỏ tay áo/cổ). Chạy lại sau kits.py.
Chạy bằng tools/match17/.venv/bin/python (cần Pillow có WebP).
"""
import json, sys
from pathlib import Path
from PIL import Image


def color(p: Path) -> str:
    im = Image.open(p).convert("RGBA")
    w, h = im.size
    box = im.crop((int(w * .33), int(h * .35), int(w * .67), int(h * .8)))
    px = [c for c in box.getdata() if c[3] > 200]
    if not px:
        return ""
    r, g, b = (sum(c[i] for c in px) // len(px) for i in range(3))
    return f"#{r:02x}{g:02x}{b:02x}"


def main(data: str) -> int:
    d = Path(data) / "kits"
    out = {p.stem: c for p in sorted(d.glob("*.webp")) if (c := color(p))}
    tmp = d / "colors.json.tmp"
    tmp.write_text(json.dumps(out, separators=(",", ":")))
    tmp.replace(d / "colors.json")
    print(len(out))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
