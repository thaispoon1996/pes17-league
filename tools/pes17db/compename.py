#!/usr/bin/env python3
"""Tên giải trên dải tiêu đề trận (PO 23/11: "đổi chữ Exhibition thành tên từng giải đấu").

Chữ "Exhibition" (dải xanh trên khung giới thiệu đội hình) là chữ của game, không phải ảnh: eng/string/all.str, mục 3378
mã 0 + mục 8 mã 107 (strtab.py). Game chạy tiếng Anh Mỹ "use" (Data/dt20_use_win.cpk) hay "eng" (gói HANO 16 đè) → làm cả
hai, thay đúng các chữ đó, đóng gói download/pes17name.cpk (sau pes17house, trước pes17compe: compe.extras). Game chỉ đọc chữ lúc mở
→ đổi giải = mở lại game (bộ chạy trận ensure_name, chỉ khi giải khác trận trước).

  compename.py build "TÊN GIẢI" RA.cpk     — in JSON {"sha", "bytes"}
  compename.py install "TÊN GIẢI"|none [--restart]   — dựng + cài / gỡ (khoá deploy.lock); in JSON {"changed", "sha"}
"""
from __future__ import annotations

import fcntl, hashlib, json, subprocess, sys
from pathlib import Path

H = Path(__file__).resolve().parent
sys.path.insert(0, str(H))
import compe  # noqa: E402
import cpk as cpklib  # noqa: E402
import dplist  # noqa: E402
import strtab  # noqa: E402

NAME = "pes17name.cpk"
# game chạy tiếng Anh Mỹ (use, theo locale Wine) hay Anh (eng) → ghi cả hai; mỗi bản lấy từ gói đang thắng của nó
REL = {"use": "use/string/all.str", "eng": "eng/string/all.str"}
SOURCES = {"use": [f"{compe.GAME}/Data/dt20_use_win.cpk"],
           "eng": [f"{compe.DL}/16-eFootballHANOV6.cpk", f"{compe.GAME}/Data/dt20_eng_win.cpk"]}
MAX = 40


def base(lang: str) -> bytes:
    """all.str đang dùng trên máy game (lưu đệm $DATA/pes17-deploy/allstr-{lang}.str)."""
    cache = compe.OUT / f"allstr-{lang}.str"
    if cache.is_file():
        return cache.read_bytes()
    for src in SOURCES[lang]:
        r = subprocess.run(["docker", "exec", "-i", "-u", "abc", compe.BENCH, "/lsiopy/bin/python3", "-", "get", src, REL[lang], "/dev/stdout"],
                           input=(H / "cpk.py").read_bytes(), capture_output=True, timeout=300)
        if r.returncode == 0 and r.stdout:
            compe.OUT.mkdir(parents=True, exist_ok=True)
            cache.write_bytes(r.stdout)
            return r.stdout
    raise RuntimeError(f"không đọc được {REL[lang]} trên máy game")


def build(name: str) -> bytes:
    name = " ".join(name.split())[:MAX]
    if not name:
        raise ValueError("tên giải trống")
    items = []
    for lang in sorted(REL):
        data = base(lang)
        for key, sid in strtab.EXHIBITION_TITLE:
            data = strtab.set_text(data, key, sid, name)
        items.append((REL[lang], data))
    return cpklib.build(compe.OUT / "template.cpk", items)


def current() -> str:
    try:
        return (compe.OUT / "name.active").read_text().strip()
    except OSError:
        return ""


def install(name: str, restart: bool) -> dict:
    OUT = compe.OUT
    with open(OUT / "deploy.lock", "w") as lk:
        fcntl.flock(lk, fcntl.LOCK_EX)
        dl = compe.DL + "/"
        put = lambda data, fn: compe.sh(f"cat > {compe.q(dl + fn)}.new && mv {compe.q(dl + fn)}.new {compe.q(dl + fn)}", data)
        if name == "none":
            if not current():
                return {"changed": False, "sha": ""}
            (OUT / "name.active").unlink(missing_ok=True)
            put(dplist.build(compe.ORIG.read_bytes(), compe.extras(bool(compe.current()))), "DpFileList.bin")
            compe.sh(f"rm -f {compe.q(dl + NAME)}")
            sha = ""
        else:
            data = build(name)
            sha = hashlib.sha256(data).hexdigest()[:16]
            if sha == current():
                return {"changed": False, "sha": sha}
            put(data, NAME)
            (OUT / "name.active").write_text(sha + "\n")
            put(dplist.build(compe.ORIG.read_bytes(), compe.extras(bool(compe.current()))), "DpFileList.bin")
    if restart:
        compe.restart_game()
    return {"changed": True, "sha": sha}


def main(argv: list[str]) -> int:
    try:
        if len(argv) == 3 and argv[0] == "build":
            data = build(argv[1])
            Path(argv[2]).write_bytes(data)
            print(json.dumps({"sha": hashlib.sha256(data).hexdigest()[:16], "bytes": len(data)}))
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
