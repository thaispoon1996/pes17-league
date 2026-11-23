#!/usr/bin/env python3
"""Bảng chữ của PES 2017 (eng/string/all.str) — đổi chữ "Exhibition" trên dải tiêu đề trận thành tên giải (PO 23/11).

Định dạng (đo 23/11 trên bản HANO 16-eFootballHANOV6.cpk, bản gốc Data/dt20_eng_win.cpk bọc WESYS + zlib):
  u32 số mục N, rồi N bộ (khoá u32, vị trí u32, cỡ u32) — các mục nối tiếp, căn 16 byte; 1188 byte giữa đầu và mục đầu không
  trỏ vị trí nào (dựng lại được). Mỗi mục: u32 số chữ C, u32 (chưa rõ, giữ), C bộ (mã u32, cỡ byte u16 gồm NUL, số ký tự u16,
  vị trí u32 tính từ đầu mục), rồi vùng chữ UTF-8 kết thúc NUL.
  Mục khoá 3378 = tên giải đấu/chế độ; mã 0 = "Exhibition" (dải tiêu đề màn giới thiệu đội hình, giao hữu).

  strtab.py get ALL.str KHOÁ MÃ
  strtab.py set ALL.str KHOÁ MÃ "CHỮ" RA.str
"""
from __future__ import annotations

import struct, sys, zlib

COMPE_KEY, EXHIBITION = 3378, 0
EXHIBITION_TITLE = [(COMPE_KEY, EXHIBITION), (8, 107)]   # 2 chữ "Exhibition" đứng riêng trong bảng


def unwrap(b: bytes) -> bytes:
    return zlib.decompress(b[16:]) if b[3:8] == b"WESYS" else b


def sections(d: bytes) -> list[tuple[int, int, int]]:
    n = struct.unpack_from("<I", d, 0)[0]
    return [struct.unpack_from("<III", d, 4 + 12 * i) for i in range(n)]


def _find(d: bytes, key: int, sid: int) -> tuple[int, int, int, int]:
    """→ (chỉ số mục, vị trí mục, cỡ mục, vị trí bộ mô tả của chữ)."""
    for i, (k, o, s) in enumerate(sections(d)):
        if k == key:
            for j in range(struct.unpack_from("<I", d, o)[0]):
                p = o + 8 + 12 * j
                if struct.unpack_from("<I", d, p)[0] == sid:
                    return i, o, s, p
            raise ValueError(f"mục {key} không có chữ mã {sid}")
    raise ValueError(f"không có mục khoá {key}")


def get(raw: bytes, key: int, sid: int) -> str:
    d = unwrap(raw)
    _, o, _, p = _find(d, key, sid)
    _, ln, _, off = struct.unpack_from("<IHHI", d, p)
    return d[o + off:o + off + ln - 1].decode("utf-8")


def set_text(raw: bytes, key: int, sid: int, text: str) -> bytes:
    """Thêm chữ mới vào cuối mục, trỏ mã sang đó, dời các mục sau (căn 16). Trả bản không bọc (game đọc được)."""
    d = unwrap(raw)
    text = text.replace("\0", "").strip()
    if not text:
        raise ValueError("chữ trống")
    idx, o, s, p = _find(d, key, sid)
    enc = text.encode("utf-8") + b"\0"
    if len(enc) > 0xFFFF:
        raise ValueError("chữ quá dài")
    sec = bytearray(d[o:o + s])
    struct.pack_into("<HHI", sec, p - o + 4, len(enc), len(text), s)
    sec += enc
    secs = sections(d)
    head_end = min(x[1] for x in secs)
    out = bytearray(d[:head_end])
    blobs = [bytes(sec) if i == idx else d[so:so + ss] for i, (_, so, ss) in enumerate(secs)]
    pos = head_end
    for i, ((k, _, _), b) in enumerate(zip(secs, blobs)):
        out += bytes(pos - len(out))
        struct.pack_into("<III", out, 4 + 12 * i, k, pos, len(b))
        out += b
        pos = (len(out) + 15) // 16 * 16
    out += bytes(pos - len(out))
    return bytes(out)


def main(argv: list[str]) -> int:
    if len(argv) == 4 and argv[0] == "get":
        print(get(open(argv[1], "rb").read(), int(argv[2]), int(argv[3])))
        return 0
    if len(argv) == 6 and argv[0] == "set":
        open(argv[5], "wb").write(set_text(open(argv[1], "rb").read(), int(argv[2]), int(argv[3]), argv[4]))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
