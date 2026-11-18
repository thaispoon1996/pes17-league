#!/bin/bash
# Tự đưa đội hình vào game PES 2017 khi DB đổi (chợ tự do, PO chọn 18/11/2026: tự động, chỉ khi game không đang đá).
# PES17_PGDB = tên DB để băm (mặc định pes17league; nhớ đặt PES17_DB cùng DB cho deploy.sh).
# Mỗi $PES17_SYNC_EVERY giây (mặc định 30): băm đội hình hiện tại (psql trong pes17league-pg-dev) → khác lần cài trước
# → nếu không đang đá: deploy.sh --restart (đội hình xuất ra giống bản đã cài thì deploy.sh bỏ qua, không khởi động lại).
# Đang đá = có file $OUT/match.lock (tools/match17/pesmatch17.py tạo/xoá) hoặc màn hình game đang là trận (match17 loop.classify: hud/setpiece). Chạy: nohup tools/pes17db/sync.sh >> ~/pes17league-data/pes17-deploy/sync.log 2>&1 &
set -uo pipefail
H=$(cd "$(dirname "$0")" && pwd)
DATA=${PES17_DATA:-$HOME/pes17league-data}
OUT="$DATA/pes17-deploy"
EVERY=${PES17_SYNC_EVERY:-30}
M17="$H/../match17"
mkdir -p "$OUT"
exec 9>"$OUT/sync.pid.lock"; flock -n 9 || { echo "sync.sh đang chạy rồi"; exit 1; }

squad_hash() {   # rỗng nếu psql lỗi
  local rows
  rows=$(docker exec pes17league-pg-dev psql -U postgres -d "${PES17_PGDB:-pes17league}" -At -c '
    select cm."PesClubIndex", pm."PesPlayerId", m."ShirtNumber", m."SlotOrder"
    from clubs."SquadMemberships" m
    join clubs."Clubs" c on c."Id" = m."ClubId" and c."Status" = $$Active$$
    join clubs."Seasons" s on s."Status" <> $$Finished$$ and s."PatchVersionId" is not null
    join clubs."ClubPesMappings" cm on cm."ClubId" = m."ClubId" and cm."PatchVersionId" = s."PatchVersionId"
    join clubs."PlayerPesMappings" pm on pm."PlayerId" = m."PlayerId" and pm."PatchVersionId" = s."PatchVersionId"
    where m."LeftAt" is null order by 1, 4') || return 0
  [ -n "$rows" ] && printf '%s' "$rows" | sha256sum | cut -c1-16
}

in_match() {
  [ -e "$OUT/match.lock" ] && return 0
  [ -x "$M17/.venv/bin/python" ] || return 1
  (cd "$M17" && .venv/bin/python - 2>/dev/null <<'E'
import sys; sys.path.insert(0, "tools")
import loop
sys.exit(0 if loop.classify(loop.grab()) in ("hud", "setpiece") else 1)
E
  )
}

log() { echo "$(date '+%F %T') $*"; }
log "bắt đầu, mỗi ${EVERY}s"
while true; do
  h=$(squad_hash)
  if [ -n "$h" ] && [ "$h" != "$(cat "$OUT/deployed.hash" 2>/dev/null)" ]; then
    if in_match; then
      log "đội hình đổi ($h) nhưng game đang đá — chờ"
    elif "$H/deploy.sh" --restart > "$OUT/deploy.last.log" 2>&1; then
      echo "$h" > "$OUT/deployed.hash"
      log "($h): $(grep -E "kept|đã cài|không đổi" "$OUT/deploy.last.log" | tr '\n' ' ')"
    else
      log "deploy LỖI — xem $OUT/deploy.last.log"; sleep 300
    fi
  fi
  sleep "$EVERY"
done
