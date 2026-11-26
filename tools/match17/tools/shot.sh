#!/usr/bin/env bash
# Chụp màn hình game (pes17-bench :1) → tools/match17/screens/<tên>.png (xwd trong container, convert trên máy chủ)
# Mọi toạ độ/mẫu (m17ocr, score, pick, m17nav, templates/) đo trên khung cũ: cửa sổ có thanh tiêu đề Openbox, game 1284x700 ở y=23.
# Cửa sổ không viền (PO 23/11, tools/bench/borderless.sh): game tự khoá cỡ 1284x700 → nằm ở y=0, dải đen 20px dưới đáy
# (đo 23/11: hàng 0 = đường viền xám 1px Openbox = hàng 23 của khung cũ) → dời xuống 23px là đúng khung cũ, khỏi đo lại.
# Nếu sau này game phủ trọn 1280x720 (H ≥ 718) → co về 1284x700 rồi dời.
# Màn to hơn (1080p, PO 26/11 đồ hoạ nét hơn): co vùng game về đúng cỡ khung 720p trước rồi đi đúng các nhánh trên → toạ độ/mẫu giữ nguyên.
set -euo pipefail
NAME="${1:-shot_$(date -u +%H%M%S)}"
OUT="$(cd "$(dirname "$0")/.." && pwd)/screens"; mkdir -p "$OUT"
TMP="$OUT/$NAME.png.tmp.png"
# dòng đầu "Y CAO" của cửa sổ game (không có cửa sổ → "0 0"), sau đó là ảnh xwd; read của bash đọc từng byte nên phần ảnh còn nguyên cho convert
docker exec -u abc -e DISPLAY=:1 pes17-bench sh -c 'xwininfo -name "Pro Evolution Soccer 2017" 2>/dev/null | awk "/Absolute upper-left Y/{y=\$4} /Height:/{h=\$2} END{print y+0, h+0}"; xwininfo -root | awk "/Width:/{print \$2}"; exec xwd -root -silent' \
  | { read -r Y H; read -r SW
      if [ "${SW:-1280}" -gt 1280 ] && [ "$Y" = 0 ] && [ "$H" -gt 0 ]; then
        # đo 26/11 ở 1920x1080: cửa sổ 1924x1060 (dải đen dưới vẫn 20px, không co theo) → vùng game 1920xH co về 1280x700 như khung 720p
        PRE=(-crop "${SW}x${H}+0+0" +repage -filter Triangle -resize '1280x700!'); H=700
      elif [ "${SW:-1280}" -gt 1280 ]; then
        Y=$(( (Y * 1280 + SW / 2) / SW )); H=$(( (H * 1280 + SW / 2) / SW ))
        PRE=(-filter Triangle -resize '1280x720!')
      else PRE=(); fi
      if [ "$Y" = 0 ] && [ "$H" -ge 718 ]; then
        convert xwd:- "${PRE[@]}" -resize '1284x700!' -background black -splice 0x23 -crop 1280x720+0+0 +repage "$TMP"
      elif [ "$Y" = 0 ] && [ "$H" -gt 0 ]; then
        convert xwd:- "${PRE[@]}" -crop 1280x697+0+0 +repage -background black -splice 0x23 "$TMP"
      else
        convert xwd:- "${PRE[@]}" "$TMP"
      fi; }
mv "$TMP" "$OUT/$NAME.png"; echo "$OUT/$NAME.png"
