#!/usr/bin/env bash
# Chụp màn hình game (pes17-bench :1) → tools/match17/screens/<tên>.png (xwd trong container, convert trên máy chủ)
# Mọi toạ độ/mẫu (m17ocr, score, pick, m17nav, templates/) đo trên khung cũ: cửa sổ có thanh tiêu đề Openbox, game 1284x700 ở y=23.
# Cửa sổ không viền (PO 23/11, tools/bench/openbox-rc.xml: game phủ 1280x720 từ y=0) → co ảnh về đúng khung cũ, khỏi đo lại.
set -euo pipefail
NAME="${1:-shot_$(date -u +%H%M%S)}"
OUT="$(cd "$(dirname "$0")/.." && pwd)/screens"; mkdir -p "$OUT"
TMP="$OUT/$NAME.png.tmp.png"
# dòng đầu "Y CAO" của cửa sổ game (không có cửa sổ → "0 0"), sau đó là ảnh xwd; read của bash đọc từng byte nên phần ảnh còn nguyên cho convert
docker exec -u abc -e DISPLAY=:1 pes17-bench sh -c 'xwininfo -name "Pro Evolution Soccer 2017" 2>/dev/null | awk "/Absolute upper-left Y/{y=\$4} /Height:/{h=\$2} END{print y+0, h+0}"; exec xwd -root -silent' \
  | { read -r Y H
      if [ "$Y" = 0 ] && [ "$H" -ge 718 ]; then
        convert xwd:- -resize '1284x700!' -background black -splice 0x23 -crop 1280x720+0+0 +repage "$TMP"
      else
        convert xwd:- "$TMP"
      fi; }
mv "$TMP" "$OUT/$NAME.png"; echo "$OUT/$NAME.png"
