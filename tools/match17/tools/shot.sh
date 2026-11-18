#!/usr/bin/env bash
# Chụp màn hình game (pes17-bench :1) → tools/match17/screens/<tên>.png (xwd trong container, convert trên máy chủ)
set -euo pipefail
NAME="${1:-shot_$(date -u +%H%M%S)}"
OUT="$(cd "$(dirname "$0")/.." && pwd)/screens"; mkdir -p "$OUT"
docker exec -u abc -e DISPLAY=:1 pes17-bench xwd -root -silent | convert xwd:- "$OUT/$NAME.png.tmp.png"
mv "$OUT/$NAME.png.tmp.png" "$OUT/$NAME.png"; echo "$OUT/$NAME.png"
