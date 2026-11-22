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
