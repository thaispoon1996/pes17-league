#!/bin/bash
# Dựng/tạo lại máy PES17 (pes17-bench) chế độ token. Vé chủ sinh 1 lần vào $DATA/bench.env (chmod 600), không in ra.
# Tạo lại container sẽ TẮT GAME — đừng chạy khi đang đá. Sau khi lên: cấp vé pad (pes-tokens.py) + mở game.
set -euo pipefail
H=$(cd "$(dirname "$0")" && pwd)
DATA=${PES17_DATA:-$HOME/pes17league-data}
ENV="$DATA/bench.env"
if [ ! -s "$ENV" ]; then
  umask 077; echo "SELKIES_MASTER_TOKEN=$(head -c 32 /dev/urandom | sha256sum | cut -c1-48)" > "$ENV"
fi
docker compose -f "$H/docker-compose.yml" --env-file "$ENV" up -d --build "$@"
for i in $(seq 30); do
  docker exec pes17-bench sh -c 'echo {} | /lsiopy/bin/python3 /usr/local/bin/pes-tokens.py' >/dev/null 2>&1 && break; sleep 2
done
docker exec -d pes17-bench bash -lc "su -s /bin/bash abc -c 'export HOME=/config DISPLAY=:1; exec /bench/run-pes17.sh' >/tmp/pes-out.log 2>&1"
echo "pes17-bench lên, đã cấp vé pad, đang mở game"
