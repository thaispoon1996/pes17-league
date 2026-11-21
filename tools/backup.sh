#!/bin/bash
# Sao lưu hằng ngày ra ổ ngoài (/data, WD Elements USB) — PO duyệt 03/11. Cron 03:30, giữ 14 bản.
#   - DB giải đấu (pg_dump -Fc từ container pes17league-pg-dev)
#   - kho OF + khoá web ($PES17_DATA), docs vận hành (~/pes17-ops/docs)
# Cron (user thaipham): 40 3 * * * … >> /data/backup/pes17league.log 2>&1 (03:40 để tránh bản pesleague 03:30)
# Dùng: tools/backup.sh   (khôi phục DB: pg_restore -d pes17league --clean db.dump)
set -euo pipefail
DEST=${BACKUP_DEST:-/data/backup/pes17league}
DATA=${PES17_DATA:-$HOME/pes17league-data}
KEEP=${BACKUP_KEEP:-14}
mountpoint -q /data || { echo "Ổ /data chưa gắn — bỏ qua" >&2; exit 1; }
umask 077
dir="$DEST/$(date +%Y%m%d-%H%M)"
mkdir -p "$dir"
fail() { rm -rf "$dir"; echo "$(date '+%F %T') LỖI sao lưu — đã xoá $dir" >&2; }
trap fail ERR   # lỗi giữa chừng → không để lại bản rỗng
docker exec pes17league-pg-dev pg_dump -U postgres -Fc pes17league > "$dir/db.dump"
[ -s "$dir/db.dump" ] || false
tar -C "$HOME" -czf "$dir/files.tgz" \
  "${DATA#$HOME/}/store" "${DATA#$HOME/}/web-keys" "${DATA#$HOME/}/web.env" pes17-ops/docs
(cd "$dir" && sha256sum db.dump files.tgz > SHA256SUMS)
ls -1d "$DEST"/*/ | head -n -"$KEEP" | xargs -r rm -rf
echo "$(date '+%F %T') OK $dir $(du -sh "$dir" | cut -f1)"
