# Máy PES17 (pes17-bench) của dự án PES 17 League. Gốc: ~/pes6/pes17-web (image pes17-web:local, chỉ đọc).
# 18/11: + libSDL2 (64 + i386) → Wine (winebus SDL) thấy đủ 4 tay cầm ảo selkies; thiếu SDL chỉ thấy 1 → không đá 2 người được.
FROM pes17-web:local
# /defaults là 700 của root, trong đó có ổ Pulse. Game (user abc) không vào được → không có tiếng và bình luận.
RUN chmod 755 /defaults
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      libsdl2-2.0-0 libsdl2-2.0-0:i386 && apt-get clean && rm -rf /var/lib/apt/lists/*
# 22/11 (PO duyệt kiểu A): logo giải góc phải trên trang chơi — pes-overlay.js đọc pes/overlay.json (bộ chạy trận ghi vào /bench/overlay).
# init-nginx chép selkies-dashboard → web mỗi lần container khởi động nên sửa ở bản gốc; cp -a giữ nguyên symlink pes.
COPY pes-overlay.js /usr/share/selkies/selkies-dashboard/pes-overlay.js
RUN sed -i 's#</body>#<script src="pes-overlay.js?v=7"></script></body>#' /usr/share/selkies/selkies-dashboard/index.html \
 && sed -i 's#<script src="src/universalTouchGamepad.js"></script>##' /usr/share/selkies/selkies-dashboard/index.html \
 && sed -i 's#src="./assets/index-D2fS95jp.js"#src="./assets/index-D2fS95jp.js?v=2"#' /usr/share/selkies/selkies-dashboard/index.html \
 && sed -i 's#hardwareAcceleration:"prefer-software"};try{if(!(await VideoDecoder.isConfigSupported(He)).supported)throw new Error(`Configuration not supported: ${JSON.stringify(He)}`);return await f.configure(He)#hardwareAcceleration:"prefer-hardware"};try{if(!(await VideoDecoder.isConfigSupported(He)).supported){He.hardwareAcceleration="prefer-software";if(!(await VideoDecoder.isConfigSupported(He)).supported)throw new Error(`Configuration not supported: ${JSON.stringify(He)}`)}return await f.configure(He)#' /usr/share/selkies/selkies-dashboard/assets/*.js \
 && grep -q 'pes-overlay.js' /usr/share/selkies/selkies-dashboard/index.html \
 && ln -s /bench/overlay /usr/share/selkies/selkies-dashboard/pes
# 24/11: người chơi rớt kết nối khi đang giữ nút/cần → tay cầm ảo selkies kẹt trạng thái (cầu thủ tự chạy, menu tự chuyển) → nhả hết khi client đóng.
# Sửa trên container đang chạy: docker cp + chạy script rồi s6-svc -r svc-selkies — game MẤT tay cầm, phải mở lại game (restart_game).
COPY selkies-padfix.py /tmp/selkies-padfix.py
RUN /lsiopy/bin/python3 /tmp/selkies-padfix.py
# 24/11: đệm mã hoá 2 khung hình để mili-giây không nhảy khi mạng rung. Có hiệu lực khi Selkies mở lại.
COPY selkies-latency.py /tmp/selkies-latency.py
RUN /lsiopy/bin/python3 /tmp/selkies-latency.py
# 25/11: hình luôn là hiện tại — trang chơi báo nhận từng khung, máy chủ bỏ hình cũ khi một người trễ > 220 ms (nhảy tới khung đầy đủ mới),
# tự hạ/nâng bitrate theo mạng; số đo mỗi 5 giây/người ở /bench/stream-stats.log. Chạy SAU selkies-latency.py.
COPY selkies-smooth.py /tmp/selkies-smooth.py
RUN /lsiopy/bin/python3 /tmp/selkies-smooth.py
# 25/11: Selkies (user abc) không tự tìm ra ổ Pulse (/defaults không thuộc abc → libpulse từ chối) → trang chơi không có tiếng. Chỉ đường thẳng.
RUN sed -i 's#^export XCURSOR_THEME=breeze#export XCURSOR_THEME=breeze\nexport PULSE_SERVER=unix:/defaults/native#' /etc/s6-overlay/s6-rc.d/svc-selkies/run \
 && grep -q PULSE_SERVER /etc/s6-overlay/s6-rc.d/svc-selkies/run
