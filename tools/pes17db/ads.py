#!/usr/bin/env python3
"""Biển quảng cáo quanh sân theo CLB chủ nhà (PO 25/11: "HLV có thể đổi được ... biển quảng cáo trên web").

Trận của mình (giao hữu) dùng bộ biển "myClub" ở mọi sân (Data/dt30_win.cpk common/bg/model/bg/bill/myclub, dò 25/11):
6 tấm 512x64 (DXT1, 6 mức mip), chạy vòng quanh sân theo thứ tự bill_texlist.xml. Gói download/pes17ads.cpk thay 6 tấm đó
(DDS không nén BGRA + đủ mip; game đọc được DDS không bọc WESYS), đứng sau gói đồ hoạ chung, trước gói giải (compe.extras).
Game chỉ đọc gói lúc mở → đổi CLB chủ nhà có biển riêng thì phải mở lại game (bộ chạy trận: pesmatch17 ensure_ads).

  ads.py build SPEC.json OUT.cpk [PREVIEW.png] — spec {"boards": [{"image": "ảnh.png"} | {"text": "CHỮ", "bg": "#rrggbb", "fg": "#rrggbb"}]}
                                                 (1–6 tấm, thiếu thì lặp lại cho đủ 6); in JSON {"sha", "files", "bytes"}
  ads.py preview SPEC.json PREVIEW.png         — chỉ ảnh xem trước (6 tấm xếp dọc)
  ads.py install OUT.cpk|none [--restart]      — cài / gỡ (khoá deploy.lock); in JSON {"changed", "sha"}
"""
from __future__ import annotations

import fcntl, hashlib, json, struct, sys
from pathlib import Path

H = Path(__file__).resolve().parent
sys.path.insert(0, str(H))
import compe  # noqa: E402
import cpk as cpklib  # noqa: E402
import dplist  # noqa: E402

NAME = "pes17ads.cpk"
W, HT, SLOTS = 512, 64, 6
TEX = [f"common/bg/model/bg/bill/myclub/texture/bill_face_my_{i:02d}_00.dds" for i in range(1, SLOTS + 1)]
FONT = H.parent / "match17" / "fonts" / "BarlowCondensed-ExtraBold.ttf"


def _hex(s: str | None, default: tuple[int, int, int]) -> tuple[int, int, int]:
    s = (s or "").lstrip("#")
    try:
        return (int(s[0:2], 16), int(s[2:4], 16), int(s[4:6], 16)) if len(s) == 6 else default
    except ValueError:
        return default


def text_board(text: str, bg: tuple[int, int, int], fg: tuple[int, int, int]):
    """Tấm chữ: nền một màu, chữ Barlow Condensed in hoa, co cho vừa (để lề 24 px)."""
    from PIL import Image, ImageDraw, ImageFont
    im = Image.new("RGB", (W, HT), bg)
    d = ImageDraw.Draw(im)
    text = " ".join(text.split()).upper()[:40] or " "
    for size in range(54, 14, -2):
        f = ImageFont.truetype(str(FONT), size)
        x0, y0, x1, y1 = d.textbbox((0, 0), text, font=f)
        if x1 - x0 <= W - 48 and y1 - y0 <= HT - 12:
            break
    d.text(((W - (x1 - x0)) / 2 - x0, (HT - (y1 - y0)) / 2 - y0), text, font=f, fill=fg)
    return im


def board(spec: dict):
    from PIL import Image, ImageOps
    if spec.get("image"):
        return ImageOps.fit(Image.open(spec["image"]).convert("RGB"), (W, HT), Image.LANCZOS)
    return text_board(str(spec.get("text") or ""), _hex(spec.get("bg"), (15, 19, 29)), _hex(spec.get("fg"), (0, 255, 135)))


def boards(spec: dict) -> list:
    items = [b for b in (spec.get("boards") or []) if isinstance(b, dict) and (b.get("image") or b.get("text"))][:SLOTS]
    if not items:
        raise ValueError("chưa có tấm biển nào")
    ims = [board(b) for b in items]
    return [ims[i % len(ims)] for i in range(SLOTS)]


def dds(im) -> bytes:
    """DDS không nén A8R8G8B8 (BGRA trong file) + đủ mip tới 1 px cạnh ngắn, như bản gốc (6 mức cho 512x64)."""
    from PIL import Image
    im = im.convert("RGBA")
    levels, w, h = [], im.width, im.height
    while True:
        levels.append(im.resize((w, h), Image.LANCZOS) if (w, h) != im.size else im)
        if w == 1 or h == 1 or len(levels) == 6:
            break
        w, h = max(1, w // 2), max(1, h // 2)
    body = b"".join(lv.tobytes("raw", "BGRA") for lv in levels)
    flags = 0x1 | 0x2 | 0x4 | 0x8 | 0x1000 | 0x20000   # CAPS HEIGHT WIDTH PITCH PIXELFORMAT MIPMAPCOUNT
    pf = struct.pack("<II4sIIIII", 32, 0x41, b"\0\0\0\0", 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)
    caps = struct.pack("<IIII", 0x1000 | 0x8 | 0x400000, 0, 0, 0)   # TEXTURE COMPLEX MIPMAP
    hdr = struct.pack("<II I I I I I", 124, flags, im.height, im.width, im.width * 4, 0, len(levels)) + b"\0" * 44 + pf + caps + b"\0" * 4
    return b"DDS " + hdr + body


def preview(spec: dict, out: Path, ims: list | None = None) -> None:
    from PIL import Image
    ims = ims or boards(spec)
    c = Image.new("RGB", (W, SLOTS * HT + (SLOTS - 1) * 6), (44, 48, 59))
    for i, im in enumerate(ims):
        c.paste(im.convert("RGB"), (0, i * (HT + 6)))
    out.parent.mkdir(parents=True, exist_ok=True)
    c.save(out)


def build(spec: dict, out: Path, prev: Path | None = None) -> dict:
    ims = boards(spec)
    items = {rel: dds(im) for rel, im in zip(TEX, ims)}
    data = cpklib.build(compe.OUT / "template.cpk", sorted(items.items()))
    tmp = out.with_name(out.name + ".tmp")
    tmp.write_bytes(data)
    tmp.replace(out)
    if prev:
        preview(spec, prev, ims)
    return {"sha": hashlib.sha256(data).hexdigest()[:16], "files": len(items), "bytes": len(data)}


def current() -> str:
    try:
        return (compe.OUT / "ads.active").read_text().strip()
    except OSError:
        return ""


def install(path: str, restart: bool) -> dict:
    OUT = compe.OUT
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / "deploy.lock", "w") as lk:
        fcntl.flock(lk, fcntl.LOCK_EX)
        orig = compe.ORIG.read_bytes()
        dl = compe.DL + "/"
        put = lambda data, name: compe.sh(f"cat > {compe.q(dl + name)}.new && mv {compe.q(dl + name)}.new {compe.q(dl + name)}", data)
        if path == "none":
            if not current():
                return {"changed": False, "sha": ""}
            (OUT / "ads.active").unlink(missing_ok=True)
            put(dplist.build(orig, compe.extras(bool(compe.current()))), "DpFileList.bin")   # bỏ khỏi danh sách trước rồi mới xoá gói
            compe.sh(f"rm -f {compe.q(dl + NAME)}")
            sha = ""
        else:
            data = Path(path).read_bytes()
            sha = hashlib.sha256(data).hexdigest()[:16]
            if sha == current():
                return {"changed": False, "sha": sha}
            put(data, NAME)
            (OUT / "ads.active").write_text(sha + "\n")
            put(dplist.build(orig, compe.extras(bool(compe.current()))), "DpFileList.bin")
    if restart:
        compe.restart_game()
    return {"changed": True, "sha": sha}


def main(argv: list[str]) -> int:
    try:
        if len(argv) >= 3 and argv[0] == "build":
            print(json.dumps(build(json.loads(Path(argv[1]).read_text()), Path(argv[2]), Path(argv[3]) if len(argv) > 3 else None)))
        elif len(argv) == 3 and argv[0] == "preview":
            preview(json.loads(Path(argv[1]).read_text()), Path(argv[2]))
            print("{}")
        elif len(argv) >= 2 and argv[0] == "install":
            print(json.dumps(install(argv[1], "--restart" in argv)))
        else:
            print(__doc__, file=sys.stderr)
            return 2
    except (ValueError, RuntimeError, OSError) as e:
        print(f"LỖI {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
