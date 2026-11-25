"""PES17: bộ đệm mã hoá còn 5 khung hình (25/11: 2 khung làm hình vỡ khi quay nhanh; trễ dồn đã do selkies-smooth.py bỏ hình lo). Đệm lớn thì hình xả một cục, mạng mất gói, mili-giây nhảy vài trăm.
Chạy lại nhiều lần không sao. Có hiệu lực khi tiến trình Selkies mở lại (đừng mở lại giữa trận)."""
import sys
from pathlib import Path

D = Path(sys.argv[1] if len(sys.argv) > 1 else "/lsiopy/lib/python3.12/site-packages/selkies")
MARK = "pes_low_latency_vbv5"
OLD = "            cs.h264_bitrate_kbps = video_bitrate * 1000  # Convert Mbps to kbps\n"
NEW = OLD + (
    "            # pes_low_latency_vbv5: 5 khung hình. Đệm mặc định cả giây rồi xả một cục.\n"
    "            cs.h264_vbv_buffer_size_kb = max(200, int(cs.h264_bitrate_kbps / max(cs.target_fps, 1) * 5))\n"
)


def main() -> None:
    f = D / "selkies.py"
    s = f.read_text()
    if MARK in s:
        print(f"{f.name}: đã vá")
        return
    OLD2 = ("            # pes_low_latency_vbv: 2 khung hình. Đệm mặc định cả giây rồi xả một cục.\n"
            "            cs.h264_vbv_buffer_size_kb = max(200, int(cs.h264_bitrate_kbps / max(cs.target_fps, 1) * 2))\n")
    s = s.replace(OLD2, "")   # bản vá cũ 24/11 (2 khung)
    if s.count(OLD) != 1:
        sys.exit(f"{f.name}: không tìm thấy đúng 1 chỗ để vá")
    f.write_text(s.replace(OLD, NEW, 1))
    print(f"{f.name}: vá xong")


if __name__ == "__main__":
    main()
