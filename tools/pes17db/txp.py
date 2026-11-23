#!/usr/bin/env python3
"""Ảnh menu PES 2017 (common/menu/**/*.bin "afp_*.apk") — đọc / thay ảnh để đưa ảnh tải lên từ web vào game (PO 23/11).

Tệp: đầu LE (1, 8, vị trí TXP2, cỡ phần sau) + tên "afp_*.apk" → khối TXP2 (big-endian) ở 0x30 (0x40 khi tên dài): bảng mục,
trong đó mỗi ảnh có cặp (cỡ nén + 8, vị trí so với TXP2); dữ liệu ảnh = (cỡ thật BE, cỡ nén BE) + luồng nén.
Luồng nén = LZSS kiểu Okumura (vòng 4096 byte, khớp 3–18, vị trí ghi đầu 0xFEE, vòng đầu toàn 0; byte cờ đọc bit thấp trước,
bit 1 = byte thường) — đo 23/11: giải đúng từng byte mọi ảnh của gói SiderX.
Ảnh giải ra = đầu TXDT 64 byte (cỡ thật ở +12, rộng/cao ở +16/+18, BE) + điểm ảnh 32 bit thứ tự A,R,G,B (ảnh 8 bit thì bỏ qua).
Tệp bọc WESYS (bản gốc trong Data/dt*.cpk) → zlib; game đọc được cả bản không bọc.

  txp.py ls TỆP.bin                 — liệt kê ảnh (số, rộng, cao)
  txp.py get TỆP.bin SỐ ẢNH.png     — xuất 1 ảnh
  txp.py put TỆP.bin SỐ ẢNH.png RA.bin [--box x,y,w,h]  — thay ảnh (co phủ đầy khung) hoặc chỉ 1 vùng (giữ độ trong suốt gốc)
Cần Pillow (tools/match17/.venv/bin/python).
"""
from __future__ import annotations

import struct, sys, zlib
from dataclasses import dataclass

N, F = 4096, 18
R0 = N - F   # 0xFEE


def unwrap(b: bytes) -> bytes:
    """Bỏ lớp WESYS (00 01 01 'WESYS' cỡ-nén cỡ-thật LE + zlib) nếu có."""
    if b[3:8] == b"WESYS":
        return zlib.decompress(b[16:])
    return b


def lzss_decode(src: bytes, size: int) -> bytes:
    ring = bytearray(N)
    r, out, i, n, flags = R0, bytearray(), 0, len(src), 0
    while len(out) < size and i < n:
        flags >>= 1
        if not flags & 0x100:
            flags = src[i] | 0xFF00
            i += 1
        if flags & 1:
            c = src[i]; i += 1
            out.append(c); ring[r] = c; r = (r + 1) & (N - 1)
        else:
            a, c2 = src[i], src[i + 1]; i += 2
            p, ln = a | ((c2 & 0xF0) << 4), (c2 & 0x0F) + 3
            for k in range(ln):
                c = ring[(p + k) & (N - 1)]
                out.append(c); ring[r] = c; r = (r + 1) & (N - 1)
    if len(out) < size:
        raise ValueError(f"luồng nén thiếu: {len(out)}/{size}")
    return bytes(out[:size])


def lzss_encode(raw: bytes) -> bytes:
    """Nén đơn giản mà game đọc được: điểm ảnh lặp (cách 4 byte) thành tham chiếu dài tới 18 byte, còn lại byte thường."""
    out = bytearray()
    n, i = len(raw), 0
    while i < n:
        flag_at = len(out)
        out.append(0)
        flag = 0
        for bit in range(8):
            if i >= n:
                break
            ln = 0
            if i >= 4 and raw[i] == raw[i - 4]:
                m = min(F, n - i)
                if raw[i:i + m] != raw[i - 4:i - 4 + m]:   # điểm ảnh lặp (chu kỳ 4): dài nhất có thể, tối đa 18
                    m = 1
                    while m < F and i + m < n and raw[i + m] == raw[i + m - 4]:
                        m += 1
                ln = m if m >= 3 else 0
            if ln:
                p = (R0 + i - 4) & (N - 1)
                out += bytes((p & 0xFF, ((p >> 4) & 0xF0) | (ln - 3)))
                i += ln
            else:
                flag |= 1 << bit
                out.append(raw[i])
                i += 1
        out[flag_at] = flag
    return bytes(out)


@dataclass
class Tex:
    k: int
    pos: int        # vị trí 8 byte (cỡ thật, cỡ nén) trong tệp
    usize: int
    csize: int
    w: int
    h: int
    head: bytes     # 64 byte đầu TXDT (đã giải)

    @property
    def argb(self) -> bool:
        return self.usize == 64 + self.w * self.h * 4


def textures(b: bytes) -> list[Tex]:
    out, i = [], b.find(b"TXDT")
    while i > 0:
        s = i - 1
        us, cs = struct.unpack_from(">II", b, s - 8)
        if 64 <= us < 1 << 28 and 0 < cs <= len(b) - s:
            head = lzss_decode(b[s:s + min(cs, 256)], 64)
            if head[:4] == b"TXDT":
                w, h = struct.unpack_from(">HH", head, 16)
                out.append(Tex(len(out), s - 8, us, cs, w, h, head))
                i = b.find(b"TXDT", s + cs)
                continue
        i = b.find(b"TXDT", i + 4)
    return out


def image(b: bytes, t: Tex):
    from PIL import Image
    if not t.argb:
        raise ValueError(f"ảnh {t.k} không phải 32 bit")
    raw = lzss_decode(b[t.pos + 8:t.pos + 8 + t.csize], t.usize)
    return Image.frombytes("RGBA", (t.w, t.h), raw[64:], "raw", "ARGB")


def replace(b: bytes, t: Tex, im) -> bytes:
    """Tệp mới: ảnh t thay bằng im (đúng t.w × t.h, RGBA); sửa bảng mục + cỡ ở đầu tệp."""
    import numpy as np
    if im.size != (t.w, t.h):
        raise ValueError(f"ảnh phải {t.w}x{t.h}")
    argb = np.asarray(im.convert("RGBA"))[..., [3, 0, 1, 2]].tobytes()   # Pillow không có bộ ghi ARGB
    payload = lzss_encode(t.head + argb)
    base = b.find(b"TXP2")   # 0x30, hoặc 0x40 khi tên gói dài (…_sa.apk)
    ts = textures(b)
    table_end = ts[0].pos

    def entry(o: Tex) -> int:
        key = struct.pack(">II", o.csize + 8, o.pos - base)
        at = b.find(key, base, table_end)
        if at < 0 or b.find(key, at + 1, table_end) >= 0:
            raise ValueError(f"không tìm thấy (hoặc trùng) mục ảnh {o.k} trong bảng TXP2")
        return at

    delta = len(payload) - t.csize
    slots = {o.k: entry(o) for o in ts}
    nb = bytearray(b[:t.pos]) + struct.pack(">II", t.usize, len(payload)) + payload + b[t.pos + 8 + t.csize:]
    struct.pack_into(">I", nb, slots[t.k], len(payload) + 8)
    for o in ts:   # ảnh nằm sau ảnh này trong tệp → dời vị trí
        if o.pos > t.pos:
            struct.pack_into(">II", nb, slots[o.k], o.csize + 8, o.pos - base + delta)
    # cỡ phần sau đầu tệp (LE ở +12) và cỡ khối TXP2 (BE ở TXP2+12): = len − TXP2, có tệp ngắn hơn vài byte (đệm cuối tệp) → cộng delta.
    # Lệch xa = trường cũ không ai sửa (bộ SiderX có bản như vậy, game vẫn chạy) → để nguyên.
    for fmt, at in (("<I", 12), (">I", base + 12)):
        v = struct.unpack_from(fmt, b, at)[0]
        if 0 <= len(b) - base - v < 64:
            struct.pack_into(fmt, nb, at, v + delta)
    return bytes(nb)


def cover(src, w: int, h: int):
    """Co phủ đầy khung w×h (cắt phần thừa ở giữa), như CSS object-fit: cover."""
    from PIL import Image
    k = max(w / src.width, h / src.height)
    im = src.convert("RGBA").resize((max(w, round(src.width * k)), max(h, round(src.height * k))), Image.LANCZOS)
    x, y = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((x, y, x + w, y + h))


def paste_box(base, src, box: tuple[int, int, int, int]):
    """Đặt src phủ đầy vùng box của base, giữ độ trong suốt gốc của vùng (góc bo, viền mờ)."""
    from PIL import Image, ImageChops
    x, y, w, h = box
    region = base.crop((x, y, x + w, y + h))
    new = cover(src, w, h)
    a = ImageChops.multiply(region.getchannel("A"), new.getchannel("A"))
    new.putalpha(a)
    out = base.copy()
    out.paste(new, (x, y))
    return out


def opaque_box(im, min_alpha: int = 200) -> tuple[int, int, int, int] | None:
    """Khung bao của vùng đục lớn nhất (dò banner trong ảnh ghép) — quét theo hàng/cột có ≥ 40% điểm đục."""
    import numpy as np
    a = np.asarray(im.getchannel("A")) >= min_alpha
    best = None
    rows = a.mean(axis=1) >= 0.2
    # các dải hàng liên tục
    y = 0
    H = a.shape[0]
    while y < H:
        if not rows[y]:
            y += 1
            continue
        y2 = y
        while y2 < H and rows[y2]:
            y2 += 1
        cols = a[y:y2].mean(axis=0) >= 0.4
        x = 0
        W = a.shape[1]
        while x < W:
            if not cols[x]:
                x += 1
                continue
            x2 = x
            while x2 < W and cols[x2]:
                x2 += 1
            area = (x2 - x) * (y2 - y)
            if best is None or area > best[2] * best[3]:
                best = (x, y, x2 - x, y2 - y)
            x = x2
        y = y2
    return best


def main(argv: list[str]) -> int:
    if len(argv) >= 2 and argv[0] == "ls":
        b = unwrap(open(argv[1], "rb").read())
        for t in textures(b):
            print(t.k, t.w, t.h, "argb" if t.argb else f"{(t.usize - 64) // max(1, t.w * t.h) * 8}bit", t.csize)
        return 0
    if len(argv) >= 4 and argv[0] == "get":
        b = unwrap(open(argv[1], "rb").read())
        image(b, textures(b)[int(argv[2])]).save(argv[3])
        return 0
    if len(argv) >= 5 and argv[0] == "put":
        from PIL import Image
        b = unwrap(open(argv[1], "rb").read())
        t = textures(b)[int(argv[2])]
        src = Image.open(argv[3])
        if "--box" in argv:
            box = tuple(int(v) for v in argv[argv.index("--box") + 1].split(","))
            im = paste_box(image(b, t), src, box)   # type: ignore[arg-type]
        else:
            im = cover(src, t.w, t.h)
        open(argv[4], "wb").write(replace(b, t, im))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
