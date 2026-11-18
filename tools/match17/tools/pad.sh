#!/usr/bin/env bash
# Gửi chuỗi nút pad vào PES17 (pes17-bench) qua Selkies. Ví dụ: tools/pad.sh "A w:1 Down Down A". Sao từ spike-league (pes17-web).
# PAD_SLOT=1 → tay cầm index 1 (P2, đội khách ở trận 2 người); mặc định 0 (P1).
set -euo pipefail
SEQ="${*:-}"
[[ -n "$SEQ" ]] || { echo "usage: pad.sh <seq...>"; exit 1; }
docker cp "$(cd "$(dirname "$0")" && pwd)/pad17.py" pes17-bench:/tmp/pad17.py >/dev/null
printf '%s\n' "$SEQ" | docker exec -i pes17-bench /lsiopy/bin/python3 /tmp/pad17.py "${PAD_SLOT:-0}"
