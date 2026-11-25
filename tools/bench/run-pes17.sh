#!/usr/bin/env bash
# Max-opt PES17: Low graphics + no HUD + Openbox host; VSync ON for stable 60 stream.
set -euo pipefail
export DISPLAY="${DISPLAY:-:1}"
export WINEPREFIX="${WINEPREFIX:-/config/.wine}"
export WINEDEBUG=-all
export SteamAppId=456610
export SteamGameId=456610
export DXVK_HUD="${DXVK_HUD:-0}"
# Tiếng (25/11): nối thẳng ổ Pulse của Selkies. Chỉ có PULSE_RUNTIME_PATH=/defaults thì thư viện Pulse đòi thư mục đó thuộc về mình (700) → user abc bị từ chối → game câm.
export PULSE_SERVER="${PULSE_SERVER:-unix:/defaults/native}"
# VSync ON (FIFO): ổn định 60 FPS game+stream, ít giật hơn IMMEDIATE trên Selkies
export DXVK_CONFIG="dxgi.syncInterval = 1
dxgi.maxFrameLatency = 1
dxgi.maxDeviceMemory = 2048
dxgi.maxSharedMemory = 8192
d3d11.relaxedBarriers = True"
# 23/11 âm thanh: xaudio2_7 (builtin) cần mfreadwrite→mfplat; tắt mfplat = game câm. Bật mfplat thì phải bật cả winegstreamer
# (game tạo bộ giải mã video MF; thiếu winegstreamer → crash). Bản cũ (không tiếng): run-pes17.sh.bak-1123-nosound
export WINEDLLOVERRIDES='d3d11,dxgi,d3d10core,d3d9=n,b;quartz=d;winemenubuilder.exe=d'
# Selkies pad: $LIB → i386 hoặc x86_64 theo bitness process (PES/Wine 32-bit)
# Cả 32+64: ld.so bỏ qua .so sai arch (PES/wine32 cần i386; wineserver cần x86_64)
export SELKIES_INTERPOSER=/bench/lib/i386-linux-gnu/selkies_joystick_interposer.so
export LD_PRELOAD="/bench/lib/i386-linux-gnu/selkies_joystick_interposer.so:/bench/lib/x86_64-linux-gnu/selkies_joystick_interposer.so:/bench/lib/i386-linux-gnu/libudev.so.1.0.0-fake:/bench/lib/x86_64-linux-gnu/libudev.so.1.0.0-fake"
export SDL_JOYSTICK_DEVICE=/dev/input/js0
# Selkies đổi màn về chế độ 1280x720 tần số 0 Hz → DXVK chia cho 0 lúc mở game (18/11). Ép mode 60 Hz nếu có.
xrandr 2>/dev/null | grep -q "1280x720_60.00" && xrandr --output screen --mode 1280x720_60.00 2>/dev/null || true
GAME="/config/.wine-pes17/drive_c/Program Files (x86)/Pro Evolution Soccer 2017"
ln -sfn "$GAME" "/config/.wine/drive_c/Program Files (x86)/Pro Evolution Soccer 2017" 2>/dev/null || true
cp /opt/dxvk/x32/*.dll "$GAME/" 2>/dev/null || true
cd "$GAME"
# Camera (25/11): PES2017.exe đã vá giới hạn thanh camera 10→50 (tools/bench/pes17-camera-patch.py; bản gốc PES2017.exe.goc-1125). Không dùng Sider: dưới Wine sider.exe tự thoát → game treo.
exec wine PES2017.exe
