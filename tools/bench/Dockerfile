# Máy PES17 (pes17-bench) của dự án PES 17 League. Gốc: ~/pes6/pes17-web (image pes17-web:local, chỉ đọc).
# 18/11: + libSDL2 (64 + i386) → Wine (winebus SDL) thấy đủ 4 tay cầm ảo selkies; thiếu SDL chỉ thấy 1 → không đá 2 người được.
FROM pes17-web:local
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      libsdl2-2.0-0 libsdl2-2.0-0:i386 && apt-get clean && rm -rf /var/lib/apt/lists/*
# 22/11 (PO duyệt kiểu A): logo giải góc phải trên trang chơi — pes-overlay.js đọc pes/overlay.json (bộ chạy trận ghi vào /bench/overlay).
# init-nginx chép selkies-dashboard → web mỗi lần container khởi động nên sửa ở bản gốc; cp -a giữ nguyên symlink pes.
COPY pes-overlay.js /usr/share/selkies/selkies-dashboard/pes-overlay.js
RUN sed -i 's#</body>#<script src="pes-overlay.js"></script></body>#' /usr/share/selkies/selkies-dashboard/index.html \
 && grep -q 'pes-overlay.js' /usr/share/selkies/selkies-dashboard/index.html \
 && ln -s /bench/overlay /usr/share/selkies/selkies-dashboard/pes
# 24/11: người chơi rớt kết nối khi đang giữ nút/cần → tay cầm ảo selkies kẹt trạng thái (cầu thủ tự chạy, menu tự chuyển) → nhả hết khi client đóng.
# Sửa trên container đang chạy: docker cp + chạy script rồi s6-svc -r svc-selkies — game MẤT tay cầm, phải mở lại game (restart_game).
COPY selkies-padfix.py /tmp/selkies-padfix.py
RUN /lsiopy/bin/python3 /tmp/selkies-padfix.py
