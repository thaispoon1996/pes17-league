#!/usr/bin/env python3
"""Nới giới hạn thanh camera PES2017.exe (Zoom/Height/Angle) từ 10 lên N (mặc định 50). PO 25/11.

Cách Sider 3.5.1 (camera.sliders.max) làm, chép lại để KHỎI chạy Sider: dưới Wine sider.exe tự thoát ngay
khi game mở → móc của Sider trỏ vào vùng nhớ đã gỡ → game treo (page fault 7A8F8xxx).
3 mẫu mã (lấy từ .data của sider.dll), mỗi mẫu 1 byte = giới hạn thanh:
  4f 74 20 4f 74 10 4f 75 41               +47  (cmp al,10)
  8b 85 cc ff ff ff 8b 8d d0 ff ff ff 50   +25  (push 10)
  4f 74 18 4f 74 0c 4f 75                  +35  (cmp al,10)
Chạy: pes17-camera-patch.py "<.../PES2017.exe>" [N]   — giữ bản gốc ở PES2017.exe.goc-1125; chạy lại không sao.
Lưu ý: save đã đặt Height > 10 → nếu trả về exe gốc thì vào game đặt lại camera.
"""
import shutil, sys
from pathlib import Path

PATS = [("4f 74 20 4f 74 10 4f 75 41", 47), ("8b 85 cc ff ff ff 8b 8d d0 ff ff ff 50", 25), ("4f 74 18 4f 74 0c 4f 75", 35)]

def main() -> None:
    exe = Path(sys.argv[1]); n = int(sys.argv[2]) if len(sys.argv) > 2 else 50
    assert 10 <= n <= 127
    d = bytearray(exe.read_bytes())
    offs = []
    for pat, o in PATS:
        b = bytes.fromhex(pat); i = d.find(b)
        if i < 0 or d.find(b, i + 1) >= 0:
            sys.exit(f"mẫu {pat}: không thấy hoặc thấy nhiều chỗ — exe khác bản đã thử")
        offs.append(i + o)
    if all(d[x] == n for x in offs):
        print("đã vá sẵn"); return
    bak = exe.with_name(exe.name + ".goc-1125")
    if not bak.exists(): shutil.copy2(exe, bak)
    for x in offs:
        d[x] = n
    exe.write_bytes(d); print("đã vá", [hex(x) for x in offs], "→", n)

if __name__ == "__main__":
    main()
