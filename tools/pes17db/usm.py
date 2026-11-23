#!/usr/bin/env python3
"""Video chuyển cảnh chiếu lại của PES 2017 (common/menu/fade/*.usm, PO 23/11 "thay đồ hoạ lúc chuyển cảnh chiếu lại").

Định dạng (đo 23/11 trên WEPESwipe_hd.usm gốc, Data/dt11_win.cpk): CRI USM = chuỗi khối [mã 4 byte][cỡ BE] + đầu 0x18 byte
(byte 1 = vị trí dữ liệu, 2–3 = đệm cuối, 7 = loại 0 dữ liệu/1 đầu/2 chú thích/3 mục lục, 8–15 = thời điểm + nhịp khung).
Tổng mỗi khối là bội 32. @SFV = hình MPEG-1 720x480, @ALP = độ trong suốt (MPEG-1 xám), mỗi khung 1 khối, xen kẽ SFV/ALP;
40 khung, khung khoá mỗi 13 khung (0, 13, 26, 39), bảng VIDEO_SEEKINFO giữ vị trí byte của các khung khoá.
Dựng: giữ nguyên các khối đầu của file gốc (CRID, VIDEO_HDRINFO, SEEKINFO — chỉ sửa số), thay khối dữ liệu bằng hình mới.

  usm.py build GỐC.usm KHUNG_DIR RA.usm   — KHUNG_DIR/00.png..39.png (RGBA 720x480); cần ffmpeg (máy chủ / container web: docker pes17league-rec)
  usm.py frames TỆP.usm RA_DIR             — tách lại hình + alpha (kiểm tra)
"""
from __future__ import annotations

import shutil, struct, subprocess, sys, tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cpk  # noqa: E402  (đọc/ghi bảng @UTF)

N, W, H, GOP = 40, 720, 480, 13
REC_IMAGE = "pes17league-rec:local"


def chunks(b: bytes) -> list[dict]:
    out, i = [], 0
    while i + 8 <= len(b):
        sig, size = b[i:i + 4], struct.unpack_from(">I", b, i + 4)[0]
        off, pad = b[i + 9], struct.unpack_from(">H", b, i + 10)[0]
        out.append({"sig": sig, "at": i, "raw": b[i:i + 8 + size], "type": b[i + 15] & 3,
                    "time": struct.unpack_from(">I", b, i + 16)[0], "rate": struct.unpack_from(">I", b, i + 20)[0],
                    "payload": b[i + 8 + off:i + 8 + size - pad]})
        i += 8 + size
    return out


def chunk(sig: bytes, dtype: int, time: int, rate: int, payload: bytes) -> bytes:
    pad = (-(8 + 0x18 + len(payload))) % 32
    head = bytes([0, 0x18]) + struct.pack(">H", pad) + bytes([0, 0, 0, dtype]) + struct.pack(">II", time, rate) + bytes(8)
    return sig + struct.pack(">I", len(head) + len(payload) + pad) + head + payload + bytes(pad)


def pictures(m1v: bytes) -> list[bytes]:
    """Tách luồng MPEG-1 theo ảnh: đầu chuỗi/GOP đi kèm ảnh ngay sau; mã kết thúc gắn vào ảnh cuối."""
    cuts, has_pic, i = [], True, 0
    while (i := m1v.find(b"\0\0\1", i)) >= 0:
        code = m1v[i + 3]
        if code in (0xB3, 0xB8, 0x00):
            if has_pic:            # khối hiện tại đã có ảnh → mã này mở khối mới
                cuts.append(i)
                has_pic = False
            if code == 0x00:
                has_pic = True
        i += 3
    cuts.append(len(m1v))
    return [m1v[a:b] for a, b in zip(cuts, cuts[1:])]


def ffmpeg(args: list[str], work: Path) -> None:
    if shutil.which("ffmpeg"):
        subprocess.run(["ffmpeg", "-v", "error", "-y", *args], cwd=work, check=True)
    else:   # container web: chưa có ffmpeg → mượn ảnh ghi hình (cùng đường dẫn trên máy chủ)
        subprocess.run(["docker", "run", "--rm", "-v", f"{work}:/w", "-w", "/w", "--entrypoint", "ffmpeg", REC_IMAGE,
                        "-v", "error", "-y", *args], check=True)


def encode(frames: Path, work: Path) -> tuple[list[bytes], list[bytes]]:
    from PIL import Image
    for i in range(N):   # màu đặt trên nền đen (như bản gốc), độ trong suốt thành ảnh xám riêng
        im = Image.open(frames / f"{i:02d}.png").convert("RGBA")
        if im.size != (W, H):
            raise ValueError(f"khung {i}: cần {W}x{H}")
        black = Image.new("RGBA", im.size, (0, 0, 0, 255))
        black.alpha_composite(im)
        black.convert("RGB").save(work / f"c{i:02d}.png")
        im.getchannel("A").save(work / f"a{i:02d}.png")
    for src, out, rate in (("c", "v.m1v", ["-b:v", "6M", "-maxrate", "8M"]), ("a", "a.m1v", ["-b:v", "3M", "-maxrate", "5M"])):
        ffmpeg(["-framerate", "30", "-i", f"{src}%02d.png", "-frames:v", str(N), "-c:v", "mpeg1video", "-g", str(GOP), "-bf", "0",
                *rate, "-bufsize", "4M", "-pix_fmt", "yuv420p", "-f", "mpeg1video", out], work)
    v, a = pictures((work / "v.m1v").read_bytes()), pictures((work / "a.m1v").read_bytes())
    if len(v) != N or len(a) != N:
        raise ValueError(f"số khung sai: hình {len(v)}, alpha {len(a)} (cần {N})")
    return v, a


def build(template: bytes, v: list[bytes], a: list[bytes]) -> bytes:
    cs = chunks(template)
    head = [c for c in cs if not (c["sig"] in (b"@SFV", b"@ALP") and c["type"] == 0)]
    data_t = {s: [c for c in cs if c["sig"] == s and c["type"] == 0] for s in (b"@SFV", b"@ALP")}
    if len(data_t[b"@SFV"]) != N or len(data_t[b"@ALP"]) != N:
        raise ValueError("file gốc không đúng khuôn 40 khung")
    end = [c for c in head if c["payload"].startswith(b"#CONTENTS END")]
    pre = [c for c in head if c not in end]
    body, pos, seek = bytearray(), sum(len(c["raw"]) for c in pre), {b"@SFV": [], b"@ALP": []}
    for k in range(N):
        for s, pics in ((b"@SFV", v), (b"@ALP", a)):
            t = data_t[s][k]
            if k % GOP == 0:
                seek[s].append({"ofs_byte": pos + len(body), "ofs_frmid": k, "num_skip": 0, "resv": 0})
            body += chunk(s, 0, t["time"], t["rate"], pics[k])
    total = pos + len(body) + sum(len(c["raw"]) for c in end)
    out = bytearray()
    for c in pre:
        p = c["payload"]
        if p[:4] == b"@UTF":
            name, cols = cpk.utf_schema(p)
            rows = cpk.read_utf(p)
            if name == "CRIUSF_DIR_STREAM":
                rows[0]["filesize"] = total
                rows[1]["filesize"], rows[2]["filesize"] = sum(map(len, v)), sum(map(len, a))
                rows[1]["minbuf"] = max(rows[1]["minbuf"], max(map(len, v)) * 2)   # bộ đệm: không nhỏ hơn bản gốc
                rows[2]["minbuf"] = max(rows[2]["minbuf"], max(map(len, a)) * 2)
            elif name == "VIDEO_HDRINFO":
                rows[0]["ixsize"] = max(rows[0]["ixsize"], max(map(len, v if c["sig"] == b"@SFV" else a)))
            elif name == "VIDEO_SEEKINFO":
                rows = seek[c["sig"]]
            np_ = cpk.write_utf(name, cols, rows)
            if len(np_) != len(p):
                raise ValueError(f"bảng {name} đổi cỡ {len(p)} → {len(np_)}")
            raw = bytearray(c["raw"])
            off = raw[9]
            raw[8 + off:8 + off + len(np_)] = np_
            out += raw
        else:
            out += c["raw"]
    out += body
    for c in end:
        out += c["raw"]
    assert len(out) == total
    return bytes(out)


def main(argv: list[str]) -> int:
    if len(argv) == 4 and argv[0] == "build":
        tpl = Path(argv[1]).read_bytes()
        with tempfile.TemporaryDirectory(prefix="usm-", dir=Path(argv[3]).resolve().parent) as td:
            v, a = encode(Path(argv[2]), Path(td))
        Path(argv[3]).write_bytes(build(tpl, v, a))
        return 0
    if len(argv) == 3 and argv[0] == "frames":
        cs = chunks(Path(argv[1]).read_bytes())
        out = Path(argv[2]); out.mkdir(parents=True, exist_ok=True)
        for s, n in ((b"@SFV", "v.m1v"), (b"@ALP", "a.m1v")):
            (out / n).write_bytes(b"".join(c["payload"] for c in cs if c["sig"] == s and c["type"] == 0))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
