#!/usr/bin/env bash
# Cửa sổ game không viền (PO 23/11): bỏ thanh tiêu đề Openbox → game phủ trọn 1280x720, người chơi Selkies thấy cả khung hình.
# Luật Openbox cho class pes2017.exe ghi vào /config/.config/openbox/rc.xml (volume máy game, giữ qua khởi động lại container;
# lấy rc.xml hệ thống + 1 luật). Luật chỉ xét lúc cửa sổ hiện → có hiệu lực khi game mở lại.
# Game tự khoá cỡ 1284x700 (không phủ trọn được, đo 23/11) → nằm ở 0,0, dải đen 20px dưới đáy. Bộ chạy không phải đo lại toạ độ:
# tools/match17/tools/shot.sh dời ảnh về khung cũ (game ở y=23).
#   tools/bench/borderless.sh on | off | status
set -euo pipefail
C="${PES17_BENCH:-pes17-bench}"
RC=/config/.config/openbox/rc.xml
RULE='  <application class="pes2017.exe"><decor>no</decor><maximized>yes</maximized><position force="yes"><x>0</x><y>0</y></position></application>'
x() { docker exec -u abc -e DISPLAY=:1 "$C" sh -c "$1"; }
case "${1:-status}" in
  on)  x "mkdir -p /config/.config/openbox && sed 's#^</applications>#$RULE\n</applications>#' /etc/xdg/openbox/rc.xml > $RC.tmp && mv $RC.tmp $RC && openbox --reconfigure"
       echo "đã bật — mở lại game để áp dụng" ;;
  off) x "rm -f $RC && openbox --reconfigure"; echo "đã tắt — mở lại game để áp dụng" ;;
  status) x "grep -q 'pes2017.exe' $RC 2>/dev/null && echo 'luật: bật' || echo 'luật: tắt'; xwininfo -name 'Pro Evolution Soccer 2017' 2>/dev/null | grep -E 'Absolute upper-left Y|Width|Height' | tr -s ' ' | paste -sd' '" ;;
  *) echo "dùng: $0 on|off|status" >&2; exit 2 ;;
esac
