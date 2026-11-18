# SDK .NET + docker CLI + python3 + ImageMagick: CLI/web gọi bộ chạy trận (docker exec vào container game, so ảnh màn hình).
# PES17 (tools/match17): Pillow + RapidOCR (onnxruntime CPU) đọc tỷ số/đồng hồ trên HUD; libgl1/libglib2.0-0 cho opencv của RapidOCR.
FROM mcr.microsoft.com/dotnet/sdk:8.0
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-pip python3-pil imagemagick libgl1 libglib2.0-0 \
 && pip3 install --no-cache-dir --break-system-packages rapidocr-onnxruntime==1.4.4 \
 && rm -rf /var/lib/apt/lists/*
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
