#!/usr/bin/env python3
"""Lấy ảnh có sẵn trong game PES17 (HANO V6) đưa lên web (PO 18/11): mặt cầu thủ, logo CLB, logo giải.

  assets.py <thư mục download> <$DATA> [--dry] [--comp]  → in lệnh SQL cập nhật LogoPath ra stdout (pipe vào psql); --comp = chỉ logo giải

- Mặt: common/render/symbol/player/player_<pesId>.dds → $DATA/faces/{PlayerId:N}.jpg (FaceStore, /img/mat).
- CLB: common/render/symbol/flag/emblem_<pesClub:04d>_r_ll.png (dự phòng _r_l, _r) → $DATA/logos/club-<pes>.png (256 px).
- Giải: common/render/symbol/emblemLc/emb_00NN_ll.png (dự phòng _l) → $DATA/logos/comp-<NN>.png.
Gói xét theo thứ tự tên (00 → 31 → pes17league.cpk): gói sau ghi đè gói trước, như game.
Ánh xạ DB lấy qua psql trong container pes17league-pg-dev (bảng PlayerPesMappings/ClubPesMappings của bản patch duy nhất).
Chạy bằng tools/match17/.venv/bin/python (cần Pillow).
"""
from __future__ import annotations

import io, re, subprocess, sys
from pathlib import Path
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cpk import Cpk  # noqa: E402

# Ánh xạ tên giải → emblemLc (đo qua ảnh ghép 58 logo: 18/11 5 giải, 19/11 thêm phần còn lại). Không có trong game: Botola Pro, Coupe du Trône, U.S. Open Cup.
COMP_EMB = {
    "Champions League": 2, "Europa League": 3,
    "Premier League": 9, "Serie A": 10, "La Liga": 11, "Ligue 1": 12, "Eredivisie": 13, "Liga Portugal": 14,
    "FA Cup": 15, "Coppa Italia": 16, "Copa del Rey": 17, "Coupe de France": 18, "KNVB Beker": 19, "Taça de Portugal": 20,
    "Brasileirão": 21, "Liga Profesional": 22, "Primera División Chile": 23, "Copa do Brasil": 24,
    "Bundesliga": 39, "MLS": 40, "Egyptian Premier League": 41, "DFB-Pokal": 42, "Egypt Cup": 44, "Copa Argentina": 49, "Copa Chile": 56,
    "Championship": 66, "LaLiga 2": 67, "Ligue 2": 68, "Serie B": 69,
}
PSQL = ["docker", "exec", "-i", "pes17league-pg-dev", "psql", "-U", "postgres", "-d", "pes17league", "-tA", "-F", "|", "-c"]


def q(sql: str) -> list[list[str]]:
    out = subprocess.run(PSQL + [sql], check=True, capture_output=True, text=True).stdout
    return [l.split("|") for l in out.splitlines() if l]


def index(download: Path) -> dict[str, tuple[Cpk, str]]:
    """tên file thường (không thư mục) → (gói, đường dẫn trong gói); gói sau đè gói trước."""
    want = re.compile(r"common/render/symbol/(player|flag|emblemlc)/")
    idx: dict[str, tuple[Cpk, str]] = {}
    for p in sorted(download.glob("*.cpk")):
        try:
            c = Cpk(p)
        except ValueError as e:   # vài gói HANO chỉ đọc được 1 phần mục lục → bỏ qua, báo ra stderr
            print(f"bỏ qua {p.name}: {e}", file=sys.stderr)
            continue
        for e in c.files:
            low = e["path"].lower()
            if want.match(low):
                idx[low] = (c, e["path"])
    return idx


def get(idx, *names: str) -> bytes | None:
    for n in names:
        hit = idx.get(n.lower())
        if hit:
            return hit[0].read(hit[1])
    return None


def png(data: bytes, size: int) -> bytes:
    im = Image.open(io.BytesIO(data)).convert("RGBA")
    im.thumbnail((size, size), Image.LANCZOS)
    b = io.BytesIO()
    im.save(b, "PNG", optimize=True)
    return b.getvalue()


def main(argv: list[str]) -> int:
    download, data, dry, only_comp = Path(argv[0]), Path(argv[1]), "--dry" in argv, "--comp" in argv
    idx = index(download)
    faces, logos = data / "faces", data / "logos"
    sql, n = [], {"face": 0, "face_miss": 0, "club": 0, "club_miss": 0, "comp": 0}

    sym = "common/render/symbol"
    for pid, pes in [] if only_comp else q('select "PlayerId","PesPlayerId" from clubs."PlayerPesMappings"'):
        raw = get(idx, f"{sym}/player/player_{pes}.dds", f"{sym}/player/player{pes}.dds")
        if raw is None:
            n["face_miss"] += 1
            continue
        n["face"] += 1
        if not dry:
            im = Image.open(io.BytesIO(raw)).convert("RGBA")
            bg = Image.new("RGB", im.size, (32, 36, 44))   # nền tối như thẻ web
            bg.paste(im, mask=im.getchannel("A"))
            bg.save(faces / f"{pid.replace('-', '')}.jpg", "JPEG", quality=90)

    for cid, pes in [] if only_comp else q('select "ClubId","PesClubIndex" from clubs."ClubPesMappings"'):
        f = f"{sym}/flag/emblem_{int(pes):04d}"
        raw = get(idx, f + "_r_ll.png", f + "_r_l.png", f + "_r.png")
        if raw is None:
            n["club_miss"] += 1
            continue
        n["club"] += 1
        name = f"club-{int(pes)}.png"
        if not dry:
            (logos / name).write_bytes(png(raw, 256))
        sql.append(f"""update clubs."Clubs" set "LogoPath"='/img/upload/{name}' where "Id"='{cid}';""")

    for cid, cname in q('select "Id","Name" from clubs."Competitions"'):
        emb = COMP_EMB.get(cname)
        raw = emb and get(idx, f"{sym}/emblemLc/emb_{emb:04d}_ll.png", f"{sym}/emblemLc/emb_{emb:04d}_l.png")
        if not raw:
            continue
        n["comp"] += 1
        name = f"comp-{emb}.png"
        if not dry:
            (logos / name).write_bytes(png(raw, 256))
        sql.append(f"""update clubs."Competitions" set "LogoPath"='/img/upload/{name}' where "Id"='{cid}';""")

    print("\n".join(sql))
    print(n, file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
