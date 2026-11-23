#!/usr/bin/env python3
"""DpFileList.bin của game = danh sách gốc HANO + gói của ta ở CUỐI (đo 18/11: gói cuối, order nhỏ nhất thắng; đặt đầu với order 33 thì KHÔNG ăn).

  dplist.py GỐC RA gói1.cpk [gói2.cpk ...]   — gói sau thắng gói trước (gói cuối order 1); gốc HANO dời order lên cho đủ chỗ.
Dùng chung cho deploy.sh (pes17league.cpk) và compe.py (pes17compe.cpk, đồ hoạ giải PO 23/11)."""
import struct, sys


def build(orig: bytes, mine: list[str]) -> bytes:
    ents = [bytearray(orig[i:i + 48]) for i in range(0, len(orig), 48)]
    ents = [e for e in ents if bytes(e[16:]).split(b"\0")[0].decode(errors="replace") not in mine]
    for e in ents:
        struct.pack_into("<I", e, 4, struct.unpack_from("<I", e, 4)[0] + len(mine))
    tail = []
    for k, name in enumerate(mine):
        me = bytearray(48)
        struct.pack_into("<I", me, 4, len(mine) - k)
        me[16:16 + len(name)] = name.encode()
        tail.append(me)
    return b"".join(ents + tail)


if __name__ == "__main__":
    o = open(sys.argv[1], "rb").read()
    open(sys.argv[2], "wb").write(build(o, sys.argv[3:]))
    print("DpFileList:", len(o) // 48, "gói HANO +", ", ".join(sys.argv[3:]))
