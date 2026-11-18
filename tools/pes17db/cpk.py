#!/usr/bin/env python3
"""Đọc gói CRI CPK (PES 2017 / HANO): liệt kê và giải nén file.

  cpk.py ls <gói.cpk> [lọc]            — liệt kê đường dẫn, cỡ
  cpk.py get <gói.cpk> <đường/dẫn> <ra> — giải nén một file
  cpk.py build <khuôn.cpk> <ra.cpk> <đường/trong/gói>=<file> ...
                                       — đóng gói mới (không nén) theo khuôn chế độ 1 (TOC+ETOC), vd 23-eFootballHANOV6.cpk

Định dạng: header "CPK " + bảng @UTF (có thể XOR-mã hoá), mục lục "TOC ".
File nén CRILAYLA khi FileSize != ExtractSize. Viết mới theo mô tả định dạng công khai.
"""
from __future__ import annotations

import struct, sys
from pathlib import Path


def _unmask(buf: bytes) -> bytes:
    out = bytearray(buf)
    m = 0x655F
    for i in range(len(out)):
        out[i] ^= m & 0xFF
        m = (m * 0x4115) & 0xFFFFFFFF
    return bytes(out)


def read_utf(raw: bytes) -> list[dict]:
    if raw[:4] != b"@UTF":
        raw = _unmask(raw)
        if raw[:4] != b"@UTF":
            raise ValueError("không phải bảng @UTF")
    be = ">"
    size, rows_off, str_off, data_off, name_off, ncol, row_len, nrows = struct.unpack(be + "IIIIIHHI", raw[4:32])
    base = 8
    strings = raw[base + str_off:]

    def cstr(off: int) -> str:
        end = strings.index(b"\0", off)
        return strings[off:end].decode("utf-8", "replace")

    fmt = {0: "B", 1: "b", 2: "H", 3: "h", 4: "I", 5: "i", 6: "Q", 7: "q", 8: "f"}

    def value(t: int, buf: bytes, pos: int):
        if t in fmt:
            n = struct.calcsize(fmt[t])
            return struct.unpack(be + fmt[t], buf[pos:pos + n])[0], pos + n
        if t == 0xA:
            return cstr(struct.unpack(be + "I", buf[pos:pos + 4])[0]), pos + 4
        if t == 0xB:
            o, n = struct.unpack(be + "II", buf[pos:pos + 8])
            return raw[base + data_off + o: base + data_off + o + n], pos + 8
        raise ValueError(f"kiểu cột lạ {t:#x}")

    cols = []
    pos = 32
    for _ in range(ncol):
        flag = raw[pos]
        name = cstr(struct.unpack(be + "I", raw[pos + 1:pos + 5])[0])
        pos += 5
        storage, t = flag & 0xF0, flag & 0x0F
        const = None
        if storage == 0x30:
            const, pos = value(t, raw, pos)
        cols.append((name, storage, t, const))
    rows = []
    for r in range(nrows):
        p = base + rows_off + r * row_len
        row = {}
        for name, storage, t, const in cols:
            if storage == 0x50:
                row[name], p = value(t, raw, p)
            elif storage == 0x30:
                row[name] = const
            else:
                row[name] = None
        rows.append(row)
    return rows


_FMT = {0: "B", 1: "b", 2: "H", 3: "h", 4: "I", 5: "i", 6: "Q", 7: "q", 8: "f"}


def utf_schema(raw: bytes) -> tuple[str, list[tuple[int, str, object]]]:
    """Tên bảng + cột (cờ, tên, hằng) của bảng @UTF — để ghi lại đúng khuôn."""
    if raw[:4] != b"@UTF":
        raw = _unmask(raw)
    _, _, str_off, data_off, name_off, ncol, _, _ = struct.unpack(">IIIIIHHI", raw[4:32])
    strings = raw[8 + str_off:]

    def cstr(o):
        return strings[o:strings.index(b"\0", o)].decode("utf-8")
    cols, pos = [], 32
    for _ in range(ncol):
        flag = raw[pos]
        name = cstr(struct.unpack(">I", raw[pos + 1:pos + 5])[0])
        pos += 5
        const = None
        if flag & 0xF0 == 0x30:
            t = flag & 0x0F
            if t == 0xA:
                const = cstr(struct.unpack(">I", raw[pos:pos + 4])[0]); pos += 4
            elif t == 0xB:
                raise ValueError("hằng kiểu data chưa hỗ trợ")
            else:
                n = struct.calcsize(_FMT[t]); const = struct.unpack(">" + _FMT[t], raw[pos:pos + n])[0]; pos += n
        cols.append((flag, name, const))
    return cstr(name_off), cols


def write_utf(table: str, cols: list[tuple[int, str, object]], rows: list[dict]) -> bytes:
    """Ghi bảng @UTF (không mã hoá). Chuỗi trùng dùng chung; tổng cỡ làm tròn 8 byte (như CRI)."""
    strtab = bytearray()
    index: dict[str, int] = {}

    def sref(v: str) -> int:
        if v not in index:
            index[v] = len(strtab)
            strtab.extend(v.encode("utf-8") + b"\0")
        return index[v]
    sref("<NULL>"); sref(table)
    data = bytearray()

    def val(t: int, v) -> bytes:
        if t == 0xA:
            return struct.pack(">I", sref("" if v is None else v))
        if t == 0xB:
            v = v or b""
            o = len(data); data.extend(v)
            return struct.pack(">II", o, len(v))
        return struct.pack(">" + _FMT[t], v)

    coldefs = bytearray()
    for flag, name, const in cols:
        coldefs += bytes([flag]) + struct.pack(">I", sref(name))
        if flag & 0xF0 == 0x30:
            coldefs += val(flag & 0x0F, const)
    rowbytes = bytearray()
    for r in rows:
        for flag, name, _ in cols:
            if flag & 0xF0 == 0x50:
                rowbytes += val(flag & 0x0F, r.get(name))
    row_len = len(rowbytes) // len(rows) if rows else 0
    rows_off = 24 + len(coldefs)
    str_off = rows_off + len(rowbytes)
    # CRI: phần đệm (tổng cỡ bội 8) nằm cuối bảng chuỗi, data ngay sau
    strtab.extend(b"\0" * ((-(8 + str_off + len(strtab) + len(data))) % 8))
    data_off = str_off + len(strtab)
    size = data_off + len(data)
    head = b"@UTF" + struct.pack(">IIIIIHHI", size, rows_off, str_off, data_off, index[table], len(cols), row_len, len(rows))
    return bytes(head + coldefs + rowbytes + strtab + data)


def _wchunk(magic: bytes, utf: bytes) -> bytes:
    return magic + struct.pack("<IQ", 0xFF, len(utf)) + utf


def build(template: str | Path, files: list[tuple[str, bytes]]) -> bytes:
    """Gói CPK chế độ 1 (TOC + ETOC), file không nén, theo khuôn header/TOC/ETOC của `template`."""
    with Path(template).open("rb") as f:
        hraw = _chunk(f, 0, b"CPK ")
        hrow = read_utf(hraw)[0]
        if hrow.get("CpkMode") != 1 or hrow.get("GtocOffset") or hrow.get("ItocOffset"):
            raise ValueError("khuôn phải là CPK chế độ 1 chỉ có TOC + ETOC")
        hname, hcols = utf_schema(hraw)
        traw = _chunk(f, hrow["TocOffset"], b"TOC ")
        tname, tcols = utf_schema(traw)
        eraw = _chunk(f, hrow["EtocOffset"], b"ETOC")
        ename, ecols = utf_schema(eraw)
        erows = read_utf(eraw)
    align = hrow["Align"]
    up = lambda n: (n + align - 1) // align * align
    files = sorted(files, key=lambda x: x[0])
    content_off = 2048
    toc_rows, blob, pos = [], bytearray(), 0
    for i, (path, data) in enumerate(files):
        d, _, n = path.rpartition("/")
        toc_rows.append({"DirName": d, "FileName": n, "FileSize": len(data), "ExtractSize": len(data),
                         "FileOffset": pos, "ID": i, "UserString": None})
        blob += data + b"\0" * (up(len(data)) - len(data))
        pos = len(blob)
    toc = _wchunk(b"TOC ", write_utf(tname, tcols, toc_rows))
    stamp = erows[0]["UpdateDateTime"]
    etoc = _wchunk(b"ETOC", write_utf(ename, ecols, [{"UpdateDateTime": stamp, "LocalDir": ""} for _ in files]
                                       + [{"UpdateDateTime": 0, "LocalDir": "pes17league"}]))
    toc_off = content_off + len(blob)
    etoc_off = up(toc_off + len(toc))
    total = sum(len(d) for _, d in files)
    hrow.update(ContentOffset=content_off, ContentSize=len(blob), TocOffset=toc_off, TocSize=len(toc),
                EtocOffset=etoc_off, EtocSize=len(etoc), EnabledPackedSize=total, EnabledDataSize=total, Files=len(files))
    head = _wchunk(b"CPK ", write_utf(hname, hcols, [hrow]))
    out = bytearray(head) + b"\0" * (content_off - len(head))
    out[content_off - 6:content_off] = b"(c)CRI"
    out += blob + toc
    out += b"\0" * (etoc_off - len(out)) + etoc
    return bytes(out)


def _chunk(f, off: int, magic: bytes) -> bytes:
    f.seek(off)
    head = f.read(16)
    if head[:4] != magic:
        raise ValueError(f"thiếu {magic!r} tại {off:#x}")
    n = struct.unpack("<Q", head[8:16])[0]
    return f.read(n)


class Cpk:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        with self.path.open("rb") as f:
            self.header = read_utf(_chunk(f, 0, b"CPK "))[0]
            toc_off = self.header.get("TocOffset") or 0
            content_off = self.header.get("ContentOffset") or toc_off
            self.files = []
            rows = read_utf(_chunk(f, toc_off, b"TOC ")) if toc_off else []  # gói chỉ có ITOC: bỏ qua
            for r in rows:
                if "FileName" not in r:
                    continue
                d = r.get("DirName") or ""
                name = f"{d}/{r['FileName']}" if d else r["FileName"]
                self.files.append({
                    "path": name,
                    "offset": min(toc_off, content_off) + r["FileOffset"],
                    "size": r["FileSize"],
                    "extract": r.get("ExtractSize") or r["FileSize"],
                })
        self.by_path = {x["path"].lower(): x for x in self.files}

    def read(self, path: str) -> bytes:
        e = self.by_path[path.lower()]
        with self.path.open("rb") as f:
            f.seek(e["offset"])
            data = f.read(e["size"])
        if e["size"] != e["extract"] or data[:8] == b"CRILAYLA":
            data = crilayla(data)
        return data


def crilayla(src: bytes) -> bytes:
    """Giải nén CRILAYLA (đọc bit ngược từ cuối, 0x100 byte đầu nằm cuối file)."""
    if src[:8] != b"CRILAYLA":
        return src
    usize, hoff = struct.unpack("<II", src[8:16])
    prefix = src[16 + hoff: 16 + hoff + 0x100]
    out = bytearray(usize)
    w = usize - 1
    pos = len(src) - 0x100 - 1  # byte cuối của dữ liệu nén
    bitbuf = 0
    bits = 0

    def get(n: int) -> int:
        nonlocal pos, bitbuf, bits
        v = 0
        while n:
            if bits == 0:
                bitbuf = src[pos]
                pos -= 1
                bits = 8
            take = min(bits, n)
            v = (v << take) | ((bitbuf >> (bits - take)) & ((1 << take) - 1))
            bits -= take
            n -= take
        return v

    end = 0
    while w >= end:
        if get(1):
            back = get(13) + 3
            length = 3
            for lvl in (2, 3, 5, 8):
                t = get(lvl)
                length += t
                if t != (1 << lvl) - 1:
                    break
            else:
                while True:
                    t = get(8)
                    length += t
                    if t != 0xFF:
                        break
            for _ in range(length):
                out[w] = out[w + back]
                w -= 1
        else:
            out[w] = get(8)
            w -= 1
    return bytes(prefix) + bytes(out)


def main(argv: list[str]) -> int:
    if len(argv) >= 2 and argv[0] == "ls":
        c = Cpk(argv[1])
        flt = argv[2].lower() if len(argv) > 2 else ""
        for e in c.files:
            if flt in e["path"].lower():
                print(f"{e['extract']:>10} {e['path']}")
        return 0
    if len(argv) == 4 and argv[0] == "get":
        Path(argv[3]).write_bytes(Cpk(argv[1]).read(argv[2]))
        return 0
    if len(argv) >= 4 and argv[0] == "build":
        files = []
        for spec in argv[3:]:
            inner, _, local = spec.partition("=")
            files.append((inner, Path(local).read_bytes()))
        out = build(argv[1], files)
        Path(argv[2]).write_bytes(out)
        c = Cpk(argv[2])
        for e in c.files:
            assert c.read(e["path"]) == dict(files)[e["path"]], e["path"]
        print(f"{argv[2]}: {len(out)} byte, {len(files)} file, đọc lại khớp")
        return 0
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
