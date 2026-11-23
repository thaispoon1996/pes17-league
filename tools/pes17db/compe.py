#!/usr/bin/env python3
"""Đồ hoạ menu theo giải (phương án D, PO 23/11) + ảnh tải lên từ web đưa vào game.

Gói = toàn bộ common/menu của 1 bộ SiderX_Switcher/<nước>/<giải> (thử 22/11: chỉ phần menu thì game chạy, đổi tông menu;
bộ đủ bg/model/demo thì game crash) + ảnh thay từ web:
  bg — nền các màn chọn đội / trước trận (system/systemBg_tex/systemBgExhibition[_sa].bin, 1920x1080), phủ đầy khung.
  (Ảnh menu tạm dừng general/pauseMenu.bin: mỗi bộ bố cục một kiểu — bảng số liệu, ô chữ — nên chưa cho thay; txp.py put --box làm được theo vùng.)
Không chọn bộ → ảnh thay đặt lên bản gốc của game (Data/dt11_win.cpk).
Bảng tỉ số trận giao hữu KHÔNG đổi được bằng ảnh (exhibition chỉ dùng mảnh chung, đo 23/11) → bảng tỉ số riêng là lớp phủ (pes-overlay.js).
Cài: download/pes17compe.cpk + DpFileList (dplist.py, gói cuối thắng) + $OUT/compe.active = mã gói. Game chỉ đọc gói lúc khởi động.

  compe.py packs                          → JSON ["England/Premier League", ...]
  compe.py preview PACK OUTDIR            → OUTDIR/bg.png (nền đang có); in JSON {"bg": [w,h]|null}
                                            PACK "-" = bản gốc của game
  compe.py build SPEC.json OUT.cpk        → spec {"pack": "England/Premier League"|null, "bg": "a.png"|null};
                                            in JSON {"sha", "files", "bytes"}
  compe.py install OUT.cpk|none [--restart]  → cài / gỡ (khoá deploy.lock chung với deploy.sh); in JSON {"changed", "sha"}
  compe.py current                        → mã gói đang cài ("" = không có)
Đọc/ghi file game qua docker exec (user abc) như deploy.sh → chạy được trên máy chủ lẫn trong container web.
Dựng gói cần Pillow + numpy (txp.py); cài/gỡ thì không.
"""
from __future__ import annotations

import fcntl, hashlib, io, json, os, subprocess, sys, tarfile
from pathlib import Path

H = Path(__file__).resolve().parent
sys.path.insert(0, str(H))
import cpk as cpklib  # noqa: E402
import dplist  # noqa: E402

BENCH = os.environ.get("PES17_BENCH", "pes17-bench")
GAME = "/config/.wine-pes17/drive_c/Program Files (x86)/Pro Evolution Soccer 2017"
DL = f"{GAME}/download"
SW = f"{GAME}/content/SiderX_Switcher"
BASE_CPK = f"{GAME}/Data/dt11_win.cpk"   # bản gốc Konami: gói duy nhất có systemBgExhibition.bin (dò 23/11 mọi gói download/ + Data/)
_store = os.environ.get("PES17_STORE")
DATA = Path(os.environ.get("PES17_DATA") or (str(Path(_store).parent) if _store else "") or Path.home() / "pes17league-data")
OUT = DATA / "pes17-deploy"
ORIG = DATA / "DpFileList.bin.orig-hano"
NAME = "pes17compe.cpk"
BG = ["common/menu/system/systemBg_tex/systemBgExhibition.bin", "common/menu/system/systemBg_tex/systemBgExhibition_sa.bin"]


def sh(cmd: str, data: bytes | None = None, check: bool = True) -> bytes:
    r = subprocess.run(["docker", "exec", "-i", "-u", "abc", BENCH, "sh", "-c", cmd], input=data, capture_output=True, timeout=300)
    if check and r.returncode:
        raise RuntimeError(r.stderr.decode(errors="replace").strip()[-300:] or f"lỗi {r.returncode}")
    return r.stdout


def q(s: str) -> str:
    return "'" + s.replace("'", "'\\''") + "'"


def safe_pack(pack: str) -> str:
    if not pack or pack.startswith("/") or ".." in pack.split("/"):
        raise ValueError(f"tên bộ không hợp lệ: {pack!r}")
    return pack


def packs() -> list[str]:
    out = sh(f"cd {q(SW)} && find . -mindepth 4 -maxdepth 4 -type d -path './*/*/common/menu' | sort")
    return [l[2:-len("/common/menu")] for l in out.decode().splitlines() if l.startswith("./")]


def pack_files(pack: str) -> dict[str, bytes]:
    """Mọi file common/menu của bộ (bỏ tên có dấu cách / .bak như lúc thử 22/11)."""
    raw = sh(f"tar -C {q(SW + '/' + safe_pack(pack))} -cf - common/menu")
    files = {}
    with tarfile.open(fileobj=io.BytesIO(raw)) as t:
        for m in t.getmembers():
            if m.isfile() and " " not in m.name and not m.name.endswith(".bak"):
                files[m.name] = t.extractfile(m).read()   # type: ignore[union-attr]
    return files


def default_file(rel: str) -> bytes | None:
    """File menu gốc của game — đọc trong gói cpk bằng python của máy game (cpk.py qua stdin)."""
    r = subprocess.run(["docker", "exec", "-i", "-u", "abc", BENCH, "/lsiopy/bin/python3", "-", "get", BASE_CPK, rel, "/dev/stdout"],
                       input=(H / "cpk.py").read_bytes(), capture_output=True, timeout=300)
    return r.stdout if r.returncode == 0 and r.stdout else None


def base(pack: str | None, rels: list[str]) -> dict[str, bytes]:
    if pack:
        files = pack_files(pack)
        return files
    return {r: b for r in rels if (b := default_file(r)) is not None}


def preview(pack: str, outdir: Path) -> dict:
    import txp
    outdir.mkdir(parents=True, exist_ok=True)
    files = base(None if pack == "-" else pack, [BG[0]])
    res: dict = {"bg": None}
    if (b := files.get(BG[0])) is not None:
        b = txp.unwrap(b)
        t = txp.textures(b)[0]
        txp.image(b, t).convert("RGB").save(outdir / "bg.png")
        res["bg"] = [t.w, t.h]
    return res


def build(spec: dict, out: Path) -> dict:
    import txp
    from PIL import Image
    pack = spec.get("pack") or None
    files = base(pack, BG)
    items: dict[str, bytes] = dict(files) if pack else {}
    if spec.get("bg"):
        src = Image.open(spec["bg"])
        for rel in BG:
            if rel in files:
                b = txp.unwrap(files[rel])
                t = txp.textures(b)[0]
                items[rel] = txp.replace(b, t, txp.cover(src, t.w, t.h))
    if not items:
        raise ValueError("gói trống: chưa chọn bộ giải, chưa có ảnh")
    data = cpklib.build(OUT / "template.cpk", sorted(items.items()))
    tmp = out.with_name(out.name + ".tmp")
    tmp.write_bytes(data)
    tmp.replace(out)
    return {"sha": hashlib.sha256(data).hexdigest()[:16], "files": len(items), "bytes": len(data)}


def extras(compe: bool) -> list[str]:
    """Thứ tự gói của mình trong DpFileList (gói sau thắng): đội hình → đồ hoạ chung mọi trận (house.py) → đồ hoạ giải."""
    out = ["pes17league.cpk"]
    if (OUT / "house.active").is_file() and (OUT / "house.active").read_text().strip():
        out.append("pes17house.cpk")
    return out + ([NAME] if compe else [])


def current() -> str:
    try:
        return (OUT / "compe.active").read_text().strip()
    except OSError:
        return ""


def restart_game() -> None:
    # như deploy.sh --restart: chờ game (cả winedbg) tắt hẳn rồi mới mở lại
    subprocess.run(["docker", "exec", BENCH, "sh", "-c",
                    "pkill -f 'PES2017[.]exe'; pkill -f 'wine[d]bg'; for i in $(seq 30); do pgrep -f 'PES2017[.]exe|wine[d]bg' >/dev/null || exit 0; "
                    "sleep 0.5; done; pkill -9 -f 'PES2017[.]exe|wine[d]bg'; true"], capture_output=True, timeout=60)
    subprocess.run(["sleep", "2"])
    subprocess.run(["docker", "exec", "-d", BENCH, "bash", "-lc",
                    "su -s /bin/bash abc -c 'export HOME=/config DISPLAY=:1; exec /bench/run-pes17.sh' >/tmp/pes-out.log 2>&1"], capture_output=True)


def install(path: str, restart: bool) -> dict:
    OUT.mkdir(parents=True, exist_ok=True)
    with open(OUT / "deploy.lock", "w") as lk:
        fcntl.flock(lk, fcntl.LOCK_EX)   # deploy.sh / bộ chạy trận không cài chồng
        orig = ORIG.read_bytes()
        put = lambda data, name: sh(f"cat > {q(DL + '/' + name)}.new && mv {q(DL + '/' + name)}.new {q(DL + '/' + name)}", data)
        if path == "none":
            if not current():
                return {"changed": False, "sha": ""}
            put(dplist.build(orig, extras(False)), "DpFileList.bin")   # bỏ khỏi danh sách trước rồi mới xoá gói
            sh(f"rm -f {q(DL + '/' + NAME)}")
            (OUT / "compe.active").unlink(missing_ok=True)
            sha = ""
        else:
            data = Path(path).read_bytes()
            sha = hashlib.sha256(data).hexdigest()[:16]
            if sha == current():
                return {"changed": False, "sha": sha}
            put(data, NAME)
            put(dplist.build(orig, extras(True)), "DpFileList.bin")
            (OUT / "compe.active").write_text(sha + "\n")
    if restart:
        restart_game()
    return {"changed": True, "sha": sha}


def main(argv: list[str]) -> int:
    if argv[:1] == ["packs"]:
        print(json.dumps(packs(), ensure_ascii=False))
    elif len(argv) == 3 and argv[0] == "preview":
        print(json.dumps(preview(argv[1], Path(argv[2]))))
    elif len(argv) == 3 and argv[0] == "build":
        print(json.dumps(build(json.loads(Path(argv[1]).read_text()), Path(argv[2]))))
    elif len(argv) >= 2 and argv[0] == "install":
        print(json.dumps(install(argv[1], "--restart" in argv)))
    elif argv[:1] == ["current"]:
        print(current())
    else:
        print(__doc__, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except (ValueError, RuntimeError, OSError) as e:
        print(f"LỖI {e}", file=sys.stderr)
        sys.exit(1)
