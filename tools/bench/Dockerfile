# Máy PES17 (pes17-bench) của dự án PES 17 League. Gốc: ~/pes6/pes17-web (image pes17-web:local, chỉ đọc).
# 18/11: + libSDL2 (64 + i386) → Wine (winebus SDL) thấy đủ 4 tay cầm ảo selkies; thiếu SDL chỉ thấy 1 → không đá 2 người được.
FROM pes17-web:local
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      libsdl2-2.0-0 libsdl2-2.0-0:i386 && apt-get clean && rm -rf /var/lib/apt/lists/*
