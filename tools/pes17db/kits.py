#!/usr/bin/env python3
"""Áo đấu CLB lấy từ game PES17 (HANO V6) lên web (PO 20/11): dựng ảnh áo nhìn thẳng từ texture áo.

  kits.py <thư mục download> <$DATA> [--dry] [--jobs N] [--only PES,PES]   → $DATA/kits/{ClubId:N}-{1st|2nd|3rd|gk}.webp (KitStore, /img/ao)

- Bộ áo mặc định của CLB: common/character0/model/character/uniform/team/<pes>/<pes>_DEF_{1st,2nd,3rd,GK1st}_realUni.bin,
  tên texture (vd "u0101p1") ở byte 0x28 → uniform/texture/<tên>.dds (2048x2048).
- Texture theo khuôn áo PES17: lưng ở trên-giữa (lộn ngược), ngực dưới-giữa, 2 tay áo ở 2 góc trên (cổ tay ở mép ngoài, đường vai giữa dải y≈385),
  tất ở giữa 2 bên, quần ở dưới 2 bên. Dựng áo = thân trước (cắt theo dáng áo) + 2 tay áo (dải mặt trước tay, nắn phối cảnh) + cổ + đổ khối + bóng đổ.
- Ánh xạ CLB lấy qua psql trong container pes17league-pg-dev (bảng ClubPesMappings). Gói đọc lỗi mục lục (06/07/19/27) → bỏ qua như assets.py.
Chạy bằng tools/match17/.venv/bin/python (cần Pillow có WebP + numpy). Mỗi áo ~3 s (chủ yếu giải nén CPK); ~1500 áo.
"""
from __future__ import annotations

import io, math, subprocess, sys
from multiprocessing import Pool
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cpk import Cpk  # noqa: E402

PSQL = ["docker", "exec", "-i", "pes17league-pg-dev", "psql", "-U", "postgres", "-d", "pes17league", "-tA", "-F", "|", "-c"]
SLOTS = {"1st": "1st", "2nd": "2nd", "3rd": "3rd", "GK1st": "gk"}   # file realUni → hậu tố ảnh web
OUT = 320   # cạnh ảnh webp (hiển thị ≤160 px CSS → đủ màn 2x)
SS = 2      # vẽ to gấp đôi rồi thu nhỏ → viền mịn


def homography(dst, src):
    """4 điểm ảnh ra → 4 điểm texture: 8 hệ số cho Image.transform PERSPECTIVE."""
    a, b = [], []
    for (x, y), (u, v) in zip(dst, src):
        a += [[x, y, 1, 0, 0, 0, -u * x, -u * y], [0, 0, 0, x, y, 1, -v * x, -v * y]]
        b += [u, v]
    return tuple(np.linalg.solve(np.array(a, float), np.array(b, float)))


def poly_mask(size, pts):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).polygon([tuple(map(float, p)) for p in pts], fill=255)
    return m


def avg(tex, box):
    return tex.crop(box).resize((1, 1), Image.BOX).getpixel((0, 0))[:3]


def render(tex: Image.Image, size: int = OUT) -> Image.Image:
    """Texture áo PES17 → ảnh áo nhìn thẳng RGBA vuông (nền trong suốt, có bóng đổ)."""
    tex = tex.convert("RGBA")
    if tex.size != (2048, 2048):
        tex = tex.resize((2048, 2048), Image.BICUBIC)
    n = 512 * SS
    s = 0.36 * SS
    cx_o, top_o = n / 2, 70 * SS

    def m(x, y):   # điểm texture (vùng thân) → điểm ảnh ra, tỉ lệ đều
        return np.array([cx_o + (x - 1024) * s, top_o + (y - 975) * s])

    out = Image.new("RGBA", (n, n), (0, 0, 0, 0))

    # thân trước: vai x 612/1436, nách 646/1402 (y 1215), gấu y 1895; cổ tròn lõm tới y 1058
    neck = [(1160, 990), (1120, 1030), (1072, 1052), (1024, 1058), (976, 1052), (928, 1030), (888, 990)]
    body_t = [(888, 990), (612, 1012), (646, 1215), (652, 1895), (1396, 1895), (1402, 1215), (1436, 1012), (1160, 990)] + neck[1:-1]
    k = homography([tuple(m(0, 0)), tuple(m(1, 0)), tuple(m(0, 1)), tuple(m(1, 1))], [(0, 0), (1, 0), (0, 1), (1, 1)])
    body = tex.transform((n, n), Image.PERSPECTIVE, k, Image.BICUBIC)

    # tay áo: dải mặt trước tay (đường vai y≈430 → nách y≈150), vai x≈668 → cổ tay x≈24; tay bên phải ảnh lấy ô góc phải (đảo ngang)
    sleeves = []
    th = math.radians(34)   # tay chếch xuống 34°
    for side in (-1, 1):
        fx = (lambda x: x) if side < 0 else (lambda x: 2048 - x)
        S = m(1024 + side * 412, 1012)   # đỉnh vai
        A = m(1024 + side * 378, 1215)   # nách
        d = np.array([side * math.cos(th), math.sin(th)])
        p = np.array([-d[1], d[0]]) * (1 if side < 0 else -1)   # vuông góc trục tay, hướng lên vai
        cc = (S + A) / 2 + d * (np.linalg.norm(A - S) * .62 + 40 * SS)
        wc = np.linalg.norm(A - S) * .40
        CS, CA = cc + p * wc, cc - p * wc
        quad = [S, A, CA, CS]
        src = [(fx(668), 430), (fx(668), 150), (fx(24), 165), (fx(24), 372)]
        layer = tex.transform((n, n), Image.PERSPECTIVE, homography([tuple(q) for q in quad], src), Image.BICUBIC)
        out.paste(layer, (0, 0), poly_mask((n, n), quad))
        sleeves.append((S, A, CA, CS))
    out.paste(body, (0, 0), poly_mask((n, n), [m(*q) for q in body_t]))

    # cổ: lòng trong (màu lưng, tối) + viền cổ
    dr = ImageDraw.Draw(out)
    inner = tuple(int(c * .42) for c in avg(tex, (990, 860, 1058, 930))) + (255,)
    dr.polygon([tuple(m(*q)) for q in neck + [(1110, 985), (1024, 978), (938, 985)]], fill=inner)
    rim = tuple(int(c * .8) for c in avg(tex, (1000, 1060, 1048, 1080))) + (255,)
    dr.line([tuple(m(*q)) for q in neck], fill=rim, width=int(14 * s), joint="curve")

    # khối: tối 2 sườn + mặt dưới tay áo, sáng giữa ngực
    alpha = out.getchannel("A")
    shade = Image.new("L", (n, n), 0)
    sd = ImageDraw.Draw(shade)
    for edge, step in ((652, 4.5), (1396, -4.5)):
        for i in range(24):
            x = edge + i * step
            sd.line([tuple(m(x, 1150)), tuple(m(x, 1900))], fill=int(80 * (1 - i / 24) ** 1.6), width=int(5 * s) + 1)
    for S, A, CA, CS in sleeves:
        sd.polygon([tuple(A), tuple(CA), tuple(CA + (CS - CA) * .35), tuple(A + (S - A) * .35)], fill=60)
    shade = shade.filter(ImageFilter.GaussianBlur(10 * SS))
    light = Image.new("L", (n, n), 0)
    ImageDraw.Draw(light).ellipse([tuple(m(860, 1000)), tuple(m(1188, 1700))], fill=34)
    light = light.filter(ImageFilter.GaussianBlur(40 * SS))
    rgb = np.asarray(out, dtype=np.float32).copy()
    sh = np.asarray(shade, np.float32)[..., None] / 255
    lt = np.asarray(light, np.float32)[..., None] / 255
    rgb[..., :3] = rgb[..., :3] * (1 - sh) + (255 - rgb[..., :3]) * lt * .5
    out = Image.fromarray(rgb.clip(0, 255).astype(np.uint8), "RGBA")

    # bóng đổ mềm dưới áo (vẽ sẵn trong ảnh → web không cần filter CSS, cuộn nhẹ máy)
    shadow = alpha.filter(ImageFilter.GaussianBlur(14 * SS)).point(lambda v: int(v * .55))
    zero = Image.new("L", (n, n), 0)
    final = Image.merge("RGBA", (zero, zero, zero, shadow)).transform((n, n), Image.AFFINE, (1, 0, 0, 0, 1, -10 * SS))
    final.alpha_composite(out)

    # cắt vuông sát áo (chừa 4%) → áo chiếm gần trọn khung
    x0, y0, x1, y1 = final.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox()
    side = max(x1 - x0, y1 - y0) * 1.04
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    box = [int(round(v)) for v in (cx - side / 2, cy - side / 2, cx + side / 2, cy + side / 2)]
    sq = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
    sq.alpha_composite(final, (max(0, -box[0]), max(0, -box[1])), (max(0, box[0]), max(0, box[1]), min(n, box[2]), min(n, box[3])))
    return sq.resize((size, size), Image.LANCZOS)


def q(sql: str) -> list[list[str]]:
    out = subprocess.run(PSQL + [sql], check=True, capture_output=True, text=True).stdout
    return [l.split("|") for l in out.splitlines() if l]


def index(download: Path) -> dict[str, tuple[str, str]]:
    """'team/..' | 'texture/..' (chữ thường, sau /uniform/) → (file gói, đường dẫn trong gói); gói sau đè gói trước như game."""
    idx: dict[str, tuple[str, str]] = {}
    for p in sorted(download.glob("*.cpk")):
        try:
            c = Cpk(p)
        except ValueError as e:
            print(f"bỏ qua {p.name}: {e}", file=sys.stderr)
            continue
        for e in c.files:
            low = e["path"].lower()
            if "/model/character/uniform/" in low:
                rest = low.split("/uniform/", 1)[1]
                if rest.startswith(("team/", "texture/")):
                    idx[rest] = (str(p), e["path"])
    return idx


_packs: dict[str, Cpk] = {}


def read(hit: tuple[str, str]) -> bytes:
    pack, path = hit
    if pack not in _packs:
        _packs[pack] = Cpk(Path(pack))
    return _packs[pack].read(path)


def job(a) -> tuple[str, str]:
    """(texture hit, file ra) → (file, lỗi hoặc "")."""
    hit, dest = a
    try:
        render(Image.open(io.BytesIO(read(hit)))).save(dest, "WEBP", quality=82, method=6)
        return dest, ""
    except Exception as e:   # texture lạ (khác khuôn, hỏng) → bỏ, web ẩn ô áo đó
        return dest, str(e)


def main(argv: list[str]) -> int:
    download, data, dry = Path(argv[0]), Path(argv[1]), "--dry" in argv
    jobs = int(argv[argv.index("--jobs") + 1]) if "--jobs" in argv else 3
    only = {int(x) for x in argv[argv.index("--only") + 1].split(",")} if "--only" in argv else None
    out = data / "kits"
    out.mkdir(parents=True, exist_ok=True)
    idx = index(download)

    todo, n = [], {"club": 0, "club_miss": 0, "kit": 0, "tex_miss": 0, "fail": 0}
    for cid, pes in q('select "ClubId","PesClubIndex" from clubs."ClubPesMappings"'):
        pes = int(pes)
        if only and pes not in only:
            continue
        got = 0
        for slot, suffix in SLOTS.items():
            ru = idx.get(f"team/{pes}/{pes}_def_{slot}_realuni.bin".lower())
            if not ru:
                continue
            name = read(ru)[0x28:0x38].split(b"\0")[0].decode("ascii", "replace")
            tex = idx.get(f"texture/{name}.dds".lower())
            if not tex:
                n["tex_miss"] += 1
                continue
            todo.append((tex, str(out / f"{cid.replace('-', '')}-{suffix}.webp")))
            got += 1
        n["club" if got else "club_miss"] += 1

    print(f"{len(todo)} áo / {n['club']} CLB (thiếu áo: {n['club_miss']} CLB)", file=sys.stderr)
    if not dry:
        with Pool(jobs) as pool:
            for i, (dest, err) in enumerate(pool.imap_unordered(job, todo, chunksize=4), 1):
                if err:
                    n["fail"] += 1
                    print(f"lỗi {dest}: {err}", file=sys.stderr)
                else:
                    n["kit"] += 1
                if i % 100 == 0:
                    print(f"… {i}/{len(todo)}", file=sys.stderr, flush=True)
    print(n, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
