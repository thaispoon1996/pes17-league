"""Logo giải góc phải trên + thẻ HLV góc dưới trái (PO 26/11, mẫu A "ảnh tách nền + thẻ tên") trên trang chơi Selkies.

Bộ chạy trận ghi vào /bench/overlay (trang đọc ở pes/): ảnh x-<sha>.png + extras.json
  {"v": số, "items": [{"src": "x-<sha>.png", "box": [x, y, w, h]}]}   — box theo màn hình thật 1280x720 (như board17)
  {"v": số, "items": []}                                                — hết trận / không có gì
Vị trí đo trên khung hình thật (cửa sổ game bắt đầu ở (1, Y+1), Y = board17.window_y):
  logo cao 72, mép trên game 14, mép phải 16; thẻ HLV đáy ở game y 612 (ngay trên thanh tên cầu thủ của game).
Trận 2 người có 2 HLV: đội nhà góc trái, đội khách góc phải (thẻ lật ngược).

  extras17.py demo face.png out.png [logo.png]   — vẽ thử thẻ HLV (+ logo) lên nền xanh
"""
from __future__ import annotations

import hashlib
import io
import json
import subprocess
import sys
import tarfile
import time
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
BOLD = ROOT / "fonts" / "BarlowCondensed-Bold.ttf"
XBOLD = ROOT / "fonts" / "BarlowCondensed-ExtraBold.ttf"
K = 2   # vẽ gấp đôi cho nét khi trang phóng to (toàn màn hình)
SURF, GREEN, MUTED, TEXT = (28, 31, 42, 235), (0, 255, 135, 255), (155, 167, 181, 255), (222, 225, 247, 255)
FACE_H, LOGO_H = 116, 72
LOGO_TOP, LOGO_RIGHT = 14, 16
CARD_LEFT, CARD_BOTTOM = 21, 612


def _font(path: Path, px: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), px * K)


def coach_card(face: str | Path, name: str, club: str, flip: bool = False) -> Image.Image:
    """Ảnh mặt tách nền đứng đè lên thẻ tên tối vạch xanh (kích thước gấp K lần)."""
    im = Image.open(face).convert("RGBA")
    if bb := im.getchannel("A").getbbox():
        im = im.crop(bb)
    fh = FACE_H * K
    im = im.resize((max(1, round(im.width * fh / im.height)), fh), Image.LANCZOS)
    f1, f2 = _font(BOLD, 13), _font(XBOLD, 24)
    top = f"HLV · {club.upper()}"[:34]
    big = name.upper()[:18]
    d0 = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    track = 13 * K * 0.08   # giãn chữ nhãn nhỏ ~0,08em
    tw = max(sum(d0.textlength(ch, font=f1) + track for ch in top), d0.textlength(big, font=f2))
    over, pad_in, pad_out = 4 * K, 14 * K, 14 * K   # thẻ lấn dưới ảnh 4px: vạch xanh sát vai
    tag_w, tag_h = int(pad_in + tw + pad_out), 56 * K
    w, h = im.width + tag_w - over, fh + 6 * K
    card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    tag = Image.new("RGBA", (tag_w, tag_h), (0, 0, 0, 0))
    td = ImageDraw.Draw(tag)
    td.rounded_rectangle((0, 0, tag_w - 1, tag_h - 1), 12 * K, fill=SURF)
    if flip:
        td.rectangle((tag_w - 12 * K, 0, tag_w - 1, tag_h - 1), fill=SURF)
        td.rectangle((tag_w - 3 * K, 0, tag_w - 1, tag_h - 1), fill=GREEN)
        tx = pad_out
    else:
        td.rectangle((0, 0, 12 * K, tag_h - 1), fill=SURF)
        td.rectangle((0, 0, 3 * K - 1, tag_h - 1), fill=GREEN)
        tx = pad_in
    x = tx
    for ch in top:   # giãn chữ nhãn nhỏ ~0,08em
        td.text((x, 6 * K), ch, font=f1, fill=MUTED)
        x += d0.textlength(ch, font=f1) + track
    td.text((tx, 22 * K), big, font=f2, fill=TEXT)
    ty = h - 6 * K - tag_h - 6 * K
    fx = w - im.width if flip else 0
    card.alpha_composite(tag, (0 if flip else im.width - over, ty))
    shadow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    shadow.putalpha(im.getchannel("A").point(lambda a: a * 150 // 255))
    card.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(4 * K)), (fx, 3 * K))
    card.alpha_composite(im, (fx, 0))
    return card


def _png(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def items(y: int, logo: str | None, coaches: list[dict], log=print, kv: float = 1.0) -> tuple[list[dict], dict[str, bytes]]:
    """(danh sách {src, box}, {tên file: dữ liệu}) — phần nào lỗi thì bỏ phần đó (trang trí là phụ)."""
    out, files = [], {}

    def add(im: Image.Image, box: list[int]) -> None:
        data = _png(im)
        name = f"x-{hashlib.sha1(data).hexdigest()[:12]}.png"
        files[name] = data
        out.append({"src": name, "box": box})

    if logo:
        try:
            lg = Image.open(logo).convert("RGBA")
            if bb := lg.getchannel("A").getbbox():
                lg = lg.crop(bb)
            lw = round(LOGO_H * lg.width / lg.height)
            add(lg.resize((lw * K, LOGO_H * K), Image.LANCZOS), [1 + 1280 - LOGO_RIGHT - lw, 1 + y + LOGO_TOP, lw, LOGO_H])
        except Exception as e:
            log(f"logo giải góc phải lỗi: {e}")
    sides = {c.get("side") for c in coaches}
    for c in coaches:
        try:
            flip = len(sides) > 1 and c.get("side") == "away"
            card = coach_card(c["face"], c.get("name") or "", c.get("club") or "", flip)
            cw, ch = card.width // K, card.height // K
            x = 1280 - CARD_LEFT - cw if flip else CARD_LEFT
            add(card, [1 + x, 1 + y + round(CARD_BOTTOM * kv) - ch, cw, ch])   # kv: board17.game_vscale (1080p)
        except Exception as e:
            log(f"thẻ HLV lỗi: {e}")
    return out, files


def publish(bench: str, overlay_dir: str, its: list[dict], files: dict[str, bytes]) -> None:
    """Ghi ảnh + extras.json (đổi tên nguyên tử) vào khung trang chơi; xoá ảnh x-* cũ."""
    doc = json.dumps({"v": int(time.time() * 1000), "items": its}).encode()
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as tar:
        for name, data in [*files.items(), ("extras.json.tmp", doc)]:
            ti = tarfile.TarInfo(name)
            ti.size, ti.mode, ti.mtime = len(data), 0o644, int(time.time())
            tar.addfile(ti, io.BytesIO(data))
    keep = " ".join(f"! -name {n}" for n in files)
    sh = (f"mkdir -p {overlay_dir} && cd {overlay_dir} && tar -xf - && mv extras.json.tmp extras.json && "
          f"find . -maxdepth 1 -name 'x-*.png' {keep} -delete")
    subprocess.run(["docker", "exec", "-i", bench, "sh", "-c", sh], input=buf.getvalue(), capture_output=True, timeout=20, check=True)


def demo(face: str, out: str, logo: str | None = None) -> None:
    sheet = Image.new("RGBA", (1280, 720), (40, 120, 60, 255))
    its, files = items(0, logo, [{"side": "home", "face": face, "name": "Thai", "club": "Aston Villa FC"},
                                 {"side": "away", "face": face, "name": "Đối thủ", "club": "R. Union Saint-Gilloise"}])
    for it in its:
        im = Image.open(io.BytesIO(files[it["src"]])).resize((it["box"][2], it["box"][3]), Image.LANCZOS)
        sheet.alpha_composite(im, (it["box"][0] - 1, it["box"][1] - 1))
    sheet.save(out)


if __name__ == "__main__":
    if len(sys.argv) >= 4 and sys.argv[1] == "demo":
        demo(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else None)
    else:
        print(__doc__)
        sys.exit(2)
