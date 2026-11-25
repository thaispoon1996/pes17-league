#!/usr/bin/env python3
"""Đồ hoạ chung mọi trận trên máy game — giao diện xanh "Pro Pitch Broadcast" (PO 23/11: "làm hết", chữ đổi được trên web).

Trận của mình (giao hữu) dùng đồ hoạ GỐC PES 2017 (Data/dt11_win.cpk; bộ SiderX_Switcher không áp, dò 23/11) → gói
download/pes17house.cpk thay các file gốc, đứng sau pes17league.cpk, trước gói giải pes17compe.cpk (compe.extras):
  - common/menu/licence/licenceTexturePes.bin — ảnh 1: khung giới thiệu đội hình / bảng thống kê (không nhãn: khung dùng chung),
    ảnh 0: dải tên đội (dải dưới thành xanh), khung thay người / thống kê. Đổi màu theo từng điểm ảnh, giữ hình dạng gốc.
  - common/menu/licence/game2dPes.bin — mảnh bảng tỉ số gốc (hiện lúc chiếu lại / ăn mừng) + 3 ô logo "PES 2017" → chữ của mình.
  - common/menu/licence/game2dPesEnter.bin — chữ "2017" đỏ của logo lúc ra sân → xanh.
  - common/menu/fade/*.usm (7 file) — chuyển cảnh chiếu lại: dải xanh quét chéo + 2 dòng chữ (usm.py dựng từ file gốc).
  - common/menu/system/systemBg_tex/systemBgExhibition[_sa].bin — nền menu: ảnh tải lên, không có thì tự vẽ.
Game chỉ đọc gói lúc mở → cài xong phải mở lại game (bộ chạy trận làm trước trận, pesmatch17 ensure_house).

  house.py build SPEC.json OUT.cpk [PREVIEW_DIR]  — spec {"line1": "PES 17", "line2": "LEAGUE", "bg": "ảnh.png"|null};
                                                     PREVIEW_DIR: wipe.png (8 khung), bg.png; in JSON {"sha", "files", "bytes"}
  house.py preview SPEC.json DIR                   — chỉ ảnh xem trước (không đọc file game)
  house.py install OUT.cpk|none [--restart]        — cài / gỡ (khoá deploy.lock); in JSON {"changed", "sha"}
"""
from __future__ import annotations

import fcntl, hashlib, json, sys, tempfile
from pathlib import Path

H = Path(__file__).resolve().parent
sys.path.insert(0, str(H))
import compe  # noqa: E402
import cpk as cpklib  # noqa: E402
import dplist  # noqa: E402
import txp  # noqa: E402
import usm  # noqa: E402

FONT = H.parent / "match17" / "fonts" / "BarlowCondensed-ExtraBold.ttf"
NAME = "pes17house.cpk"
LIC, HUD, ENTER = (f"common/menu/licence/{n}.bin" for n in ("licenceTexturePes", "game2dPes", "game2dPesEnter"))
FADE = [f"common/menu/fade/{n}.usm" for n in ("WEPESwipe_hd", "LG1wipe", "LG2wipe", "UCLwipe_hd", "UELwipe_hd", "ACLwipe_hd", "USCwipe_hd")]
BG, SURF, SURF2, LINE, RIM = (15, 19, 29), (28, 31, 42), (38, 42, 53), (44, 48, 59), (74, 82, 100)
GREEN, TEXT = (0, 255, 135), (222, 225, 247)
# ảnh gốc (đo 23/11): licence ảnh 0 = 1824x724, dải dưới của băng tên đội ở (12,620,1118,100); HUD 510x228, ô logo PES 2017
BAND = (12, 620, 1118, 100)
LOGO_PILLS = [(5, 64, 320, 47), (5, 118, 320, 47), (5, 172, 320, 47)]


def font(px: int):
    from PIL import ImageFont
    return ImageFont.truetype(str(FONT), px)


def lines(spec: dict) -> tuple[str, str]:
    l1 = (spec.get("line1") or "PES 17").strip()[:16].upper()
    l2 = (spec.get("line2") or "").strip()[:24].upper()
    return l1, l2


# ——— chuyển cảnh chiếu lại ———

def wipe_frames(spec: dict) -> list:
    """40 khung RGBA 720x480. 0–15: 3 dải tràn vào từ trái; từ 14: chữ hiện + phóng dần; khung cuối phủ kín như bản gốc
    (game cắt sang cảnh chiếu lại ở đó)."""
    from PIL import Image, ImageDraw
    W, Hh, SK = usm.W, usm.H, 0.42
    l1, l2 = lines(spec)
    ease = lambda t: (lambda u: u * u * (3 - 2 * u))(min(max(t, 0.0), 1.0))
    span = W + Hh * SK + 40
    out = []
    for i in range(usm.N):
        im = Image.new("RGBA", (W, Hh), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        for col, lag in ((GREEN, 0.0), (SURF2, 0.10), (BG, 0.20)):
            right = -Hh * SK + ease(i / 15.0 - lag * 1.2) * (span + Hh * SK)
            d.polygon([(-40, 0), (right + Hh * SK, 0), (right, Hh), (-40, Hh)], fill=col + (255,))
        if i >= 14:
            t = ease((i - 14) / 6)
            a = int(255 * t)
            sx = -200 + (i - 14) / 25.0 * (W + 400)
            d.polygon([(sx + Hh * SK, 0), (sx + Hh * SK + 26, 0), (sx + 26, Hh), (sx, Hh)], fill=SURF2 + (255,))
            d.polygon([(sx + Hh * SK + 40, 0), (sx + Hh * SK + 46, 0), (sx + 46, Hh), (sx + 40, Hh)], fill=GREEN + (255,))
            txt = Image.new("RGBA", (W, Hh), (0, 0, 0, 0))
            td = ImageDraw.Draw(txt)
            f1 = font(96 if len(l1) <= 9 else max(48, int(96 * 9 / len(l1))))
            y1 = Hh / 2 - (18 if l2 else 0)
            td.text((W / 2, y1), l1, font=f1, fill=TEXT + (a,), anchor="mm")
            if l2:
                td.text((W / 2, Hh / 2 + 52), l2, font=font(44 if len(l2) <= 16 else 32), fill=GREEN + (a,), anchor="mm")
            bw = int(160 * t)
            td.rectangle((W / 2 - bw, Hh / 2 + 86, W / 2 + bw, Hh / 2 + 90), fill=GREEN + (a,))
            z = 0.9 + 0.14 * ease((i - 14) / 25)
            zw, zh = int(W * z), int(Hh * z)
            txt = txt.resize((zw, zh), Image.LANCZOS)
            if z <= 1:
                im.alpha_composite(txt, ((W - zw) // 2, (Hh - zh) // 2))
            else:
                x, y = (zw - W) // 2, (zh - Hh) // 2
                im.alpha_composite(txt.crop((x, y, x + W, y + Hh)))
        out.append(im)
    return out


# ——— đổi màu ảnh gốc ———

def recolor(im, band: tuple[int, int, int, int] | None = None):
    """Giữ hình dạng (alpha) gốc: phần xanh ngọc (nền kính, viền tối) → nền tối, đậm hơn; phần sáng ít màu (viền bóng, dải xám)
    → viền xám xanh; trong `band`: dải xám → xanh sân. Màu khác (thẻ đỏ/vàng, mũi tên) giữ nguyên."""
    import numpy as np
    from PIL import Image
    a = np.asarray(im.convert("RGBA")).astype(np.float32)
    rgb, al = a[..., :3], a[..., 3]
    L = rgb.mean(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)
    teal = (rgb[..., 2] - rgb[..., 0] > 20) & (L < 150)
    light = (sat < 45) & (L >= 110)
    out = a.copy()
    out[teal, :3] = BG
    out[teal, 3] = np.minimum(255, al[teal] * 1.2)
    out[light & ~teal, :3] = RIM
    if band:
        x, y, w, h = band
        m = np.zeros(L.shape, bool)
        m[y:y + h, x:x + w] = True
        g = m & light & (L < 200) & (al > 200)
        out[g, :3] = GREEN
    gloss = (al < 40) & (L > 150)          # vệt bóng trắng mờ của kính → bỏ
    out[gloss, 3] = 0
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGBA")


def reskin_licence(b: bytes) -> bytes:
    b = txp.unwrap(b)
    ts = txp.textures(b)
    im0 = recolor(txp.image(b, ts[0]), BAND)
    b = txp.replace(b, ts[0], im0)
    ts = txp.textures(b)
    # Không vẽ nhãn vào khung (PO 25/11): game dùng chung khung này cho bảng thống kê nghỉ giữa hiệp → nhãn đè lên số liệu.
    im1 = recolor(txp.image(b, ts[1]))
    return txp.replace(b, ts[1], im1)


def reskin_hud(b: bytes, spec: dict) -> bytes:
    from PIL import ImageDraw
    b = txp.unwrap(b)
    t = txp.textures(b)[0]
    im = recolor(txp.image(b, t))
    d = ImageDraw.Draw(im)
    l1, l2 = lines(spec)
    for x, y, w, h in LOGO_PILLS:   # ô logo "PES 2017" → chữ của mình
        d.rectangle((x, y, x + w, y + h), fill=(0, 0, 0, 0))
        d.rounded_rectangle((x + 1, y + 1, x + w - 2, y + h - 2), radius=12, fill=BG + (240,), outline=LINE + (255,), width=2)
        f = font(34)
        w1 = d.textlength(l1, font=f)
        w2 = d.textlength(" " + l2, font=f) if l2 else 0
        x0 = x + w / 2 - (w1 + w2) / 2
        d.text((x0, y + h / 2), l1, font=f, fill=TEXT + (255,), anchor="lm")
        if l2:
            d.text((x0 + w1, y + h / 2), " " + l2, font=f, fill=GREEN + (255,), anchor="lm")
    return txp.replace(b, t, im)


ENTER_MARKS = [(4, 255, 245, 36), (256, 255, 245, 36)]   # 2 chữ "PES2017" (dưới khung đội hình, màn ra sân); x, y, rộng, cao


def reskin_enter(b: bytes, spec: dict) -> bytes:
    """Chữ "2017" đỏ → xanh; 2 chữ "PES2017" → chữ của mình (dòng 1 trắng + dòng 2 xanh, co vừa ô)."""
    import numpy as np
    from PIL import Image, ImageDraw
    b = txp.unwrap(b)
    t = txp.textures(b)[0]
    a = np.asarray(txp.image(b, t)).copy()
    red = (a[..., 0].astype(int) > a[..., 1].astype(int) + 80) & (a[..., 0].astype(int) > a[..., 2].astype(int) + 80)
    a[red, :3] = GREEN
    im = Image.fromarray(a, "RGBA")
    l1, l2 = lines(spec)
    d = ImageDraw.Draw(im)
    for x, y, w, h in ENTER_MARKS:
        d.rectangle((x, y, x + w, y + h), fill=(0, 0, 0, 0))
        px = 44
        while px > 12:
            f = font(px)
            w1 = d.textlength(l1, font=f)
            tw = w1 + (d.textlength(" " + l2, font=f) if l2 else 0)
            box = d.textbbox((0, 0), l1 + (" " + l2 if l2 else ""), font=f, anchor="ls")
            if tw <= w - 2 and box[3] - box[1] <= h - 2:
                break
            px -= 1
        x0, base = x + (w - tw) / 2, y + h / 2 + (box[3] - box[1]) / 2 - box[3]
        d.text((x0, base), l1, font=f, fill=TEXT + (255,), anchor="ls")
        if l2:
            d.text((x0 + w1, base), " " + l2, font=f, fill=GREEN + (255,), anchor="ls")
    return txp.replace(b, t, im)


def default_bg(spec: dict, w: int = 1920, h: int = 1080):
    """Nền menu tự vẽ: nền tối, dải chéo như chuyển cảnh, chữ nhỏ góc dưới phải (giao diện menu của game nằm đè lên giữa)."""
    from PIL import Image, ImageDraw
    im = Image.new("RGBA", (w, h), BG + (255,))
    d = ImageDraw.Draw(im)
    sk = 0.42
    for x0, wd, col in ((1180, 260, SURF), (1470, 90, SURF2), (1590, 14, GREEN)):
        d.polygon([(x0 + h * sk, 0), (x0 + h * sk + wd, 0), (x0 + wd, h), (x0, h)], fill=col + (255,))
    l1, l2 = lines(spec)
    d.text((w - 72, h - 118), l1, font=font(88), fill=TEXT + (255,), anchor="rs")
    if l2:
        d.text((w - 72, h - 64), l2, font=font(44), fill=GREEN + (255,), anchor="rs")
    return im


def preview(spec: dict, outdir: Path) -> None:
    from PIL import Image
    outdir.mkdir(parents=True, exist_ok=True)
    fr = wipe_frames(spec)
    pick = [3, 7, 11, 15, 20, 26, 32, 39]
    sheet = Image.new("RGB", (4 * 360 + 5 * 8, 2 * 240 + 3 * 8), (40, 110, 60))   # nền xanh cỏ: thấy phần trong suốt
    for n, i in enumerate(pick):
        sheet.paste(fr[i].resize((360, 240), Image.LANCZOS), (8 + (n % 4) * 368, 8 + (n // 4) * 248), fr[i].resize((360, 240), Image.LANCZOS))
    sheet.save(outdir / "wipe.png")
    bg = Image.open(spec["bg"]) if spec.get("bg") else default_bg(spec)
    txp.cover(bg, 1920, 1080).convert("RGB").resize((960, 540), Image.LANCZOS).save(outdir / "bg.png")


def build(spec: dict, out: Path, prev: Path | None = None) -> dict:
    from PIL import Image
    rels = [LIC, HUD, ENTER, FADE[0]] + compe.BG
    base = compe.base(None, rels)
    missing = [r for r in rels if r not in base]
    if missing:
        raise ValueError(f"không đọc được file gốc: {missing}")
    items: dict[str, bytes] = {LIC: reskin_licence(base[LIC]), HUD: reskin_hud(base[HUD], spec), ENTER: reskin_enter(base[ENTER], spec)}
    with tempfile.TemporaryDirectory(prefix="house-", dir=str(out.resolve().parent)) as td:
        tdp = Path(td)
        for i, f in enumerate(wipe_frames(spec)):
            f.save(tdp / f"{i:02d}.png")
        enc = tdp / "enc"
        enc.mkdir()
        v, a = usm.encode(tdp, enc)
    wipe = usm.build(base[FADE[0]], v, a)
    for rel in FADE:
        items[rel] = wipe
    src = Image.open(spec["bg"]) if spec.get("bg") else default_bg(spec)
    for rel in compe.BG:
        b = txp.unwrap(base[rel])
        t = txp.textures(b)[0]
        items[rel] = txp.replace(b, t, txp.cover(src, t.w, t.h))
    data = cpklib.build(compe.OUT / "template.cpk", sorted(items.items()))
    tmp = out.with_name(out.name + ".tmp")
    tmp.write_bytes(data)
    tmp.replace(out)
    if prev:
        preview(spec, prev)
    return {"sha": hashlib.sha256(data).hexdigest()[:16], "files": len(items), "bytes": len(data)}


def current() -> str:
    try:
        return (compe.OUT / "house.active").read_text().strip()
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
            (OUT / "house.active").unlink(missing_ok=True)
            put(dplist.build(orig, compe.extras(bool(compe.current()))), "DpFileList.bin")   # bỏ khỏi danh sách trước rồi mới xoá gói
            compe.sh(f"rm -f {compe.q(dl + NAME)}")
            sha = ""
        else:
            data = Path(path).read_bytes()
            sha = hashlib.sha256(data).hexdigest()[:16]
            if sha == current():
                return {"changed": False, "sha": sha}
            put(data, NAME)
            (OUT / "house.active").write_text(sha + "\n")
            put(dplist.build(orig, compe.extras(bool(compe.current()))), "DpFileList.bin")
    if restart:
        compe.restart_game()
    return {"changed": True, "sha": sha}


def main(argv: list[str]) -> int:
    try:
        if len(argv) >= 3 and argv[0] == "build":
            res = build(json.loads(Path(argv[1]).read_text()), Path(argv[2]), Path(argv[3]) if len(argv) > 3 else None)
            print(json.dumps(res))
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
