#!/bin/bash
# Đội hình DB → game PES 2017 (không Sider, PO duyệt 18/11/2026):
#   1. Pes17League.Cli export-pes17-squads → squads.json   (hoặc --squads FILE: dùng file có sẵn, vd ảnh chụp vòng từ bộ chạy trận)
#   2. đội hình giống bản đã cài (deployed.squads.sha) → thôi, không đóng gói/khởi động lại (trừ --force)
#   3. pesdb.py apply (pesdb gốc HANO) → PlayerAssignment.bin (+ TacticsFormation.bin khi có _formations, Tactics.bin khi có _plan)
#   4. cpk.py build → pes17league.cpk (khuôn gói HANO 23, đọc từ game)
#   5. ghi vào download/ của game qua docker exec + DpFileList.bin = danh sách gốc HANO (order +1) + pes17league.cpk CUỐI, order 1
#      (dplist.py; đang cài gói đồ hoạ giải compe.py thì thêm pes17compe.cpk sau cùng)
#      (đã thử 18/11: gói cuối, order nhỏ nhất thắng; đặt đầu với order 33 thì KHÔNG ăn)
#   --restart: tắt + mở lại game (game chỉ đọc gói lúc khởi động). Đừng dùng khi đang đá (bộ chạy trận tự giữ match.lock).
# Ghi file game bằng docker exec (user abc) → chạy được trên máy chủ lẫn trong container web (bộ chạy trận tools/match17).
# Biến: PES17_DATA (mặc định thư mục cha của PES17_STORE, rồi ~/pes17league-data), PES17_BENCH (pes17-bench).
set -euo pipefail
H=$(cd "$(dirname "$0")" && pwd)
R=$(cd "$H/../.." && pwd)
DATA=${PES17_DATA:-${PES17_STORE:+$(dirname "$PES17_STORE")}}
DATA=${DATA:-$HOME/pes17league-data}
BENCH=${PES17_BENCH:-pes17-bench}
DL="/config/.wine-pes17/drive_c/Program Files (x86)/Pro Evolution Soccer 2017/download"
OUT="$DATA/pes17-deploy"
ORIG="$DATA/DpFileList.bin.orig-hano"
SQUADS="" RESTART="" FORCE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --squads) SQUADS=$2; shift ;;
    --restart) RESTART=1 ;;
    --force) FORCE=1 ;;
    *) echo "không hiểu: $1" >&2; exit 2 ;;
  esac; shift
done
mkdir -p "$OUT"
[ -f "$ORIG" ] || { echo "thiếu $ORIG (DpFileList gốc HANO)" >&2; exit 1; }
exec 8>"$OUT/deploy.lock"; flock 8   # sync.sh và bộ chạy trận không cài chồng nhau

if [ -n "$SQUADS" ]; then cp "$SQUADS" "$OUT/squads.json.new"
else "$R/tools/cli.sh" export-pes17-squads "$OUT/squads.json.new"; fi
SHA=$(python3 -c 'import json,sys,hashlib; print(hashlib.sha256(json.dumps(json.load(open(sys.argv[1])),sort_keys=True).encode()).hexdigest()[:16])' "$OUT/squads.json.new")
mv "$OUT/squads.json.new" "$OUT/squads.json"
if [ -z "$FORCE" ] && [ "$SHA" = "$(cat "$OUT/deployed.squads.sha" 2>/dev/null)" ]; then
  echo "không đổi ($SHA): game đã có đội hình này"; exit 0
fi

python3 "$H/pesdb.py" apply "$DATA/pesdb" "$OUT/squads.json" "$OUT/PlayerAssignment.bin"
docker exec -u abc "$BENCH" cat "$DL/23-eFootballHANOV6.cpk" > "$OUT/template.cpk"
FILES=("common/etc/pesdb/PlayerAssignment.bin=$OUT/PlayerAssignment.bin")
# sơ đồ web (khoá _formations trong squads.json, PO 21/11) → pesdb.py apply ghi TacticsFormation.bin cạnh PlayerAssignment.bin
[ -f "$OUT/TacticsFormation.bin" ] && FILES+=("common/etc/pesdb/TacticsFormation.bin=$OUT/TacticsFormation.bin")
[ -f "$OUT/Tactics.bin" ] && FILES+=("common/etc/pesdb/Tactics.bin=$OUT/Tactics.bin")   # thông số chiến thuật (khoá _plan, PO 21/11)
python3 "$H/cpk.py" build "$OUT/template.cpk" "$OUT/pes17league.cpk" "${FILES[@]}"
# gói đồ hoạ giải đang cài (compe.py, PO 23/11) giữ nguyên trong danh sách
# + gói đồ hoạ chung mọi trận (house.py, PO 23/11) trước gói giải
EXTRA=(pes17league.cpk); [ -s "$OUT/house.active" ] && EXTRA+=(pes17house.cpk); [ -s "$OUT/ads.active" ] && EXTRA+=(pes17ads.cpk); [ -s "$OUT/name.active" ] && EXTRA+=(pes17name.cpk); [ -s "$OUT/compe.active" ] && EXTRA+=(pes17compe.cpk)
python3 "$H/dplist.py" "$ORIG" "$OUT/DpFileList.bin" "${EXTRA[@]}"
put() { docker exec -i -u abc "$BENCH" sh -c 'cat > "$1.new" && mv "$1.new" "$1"' sh "$DL/$2" < "$1"; }
put "$OUT/pes17league.cpk" pes17league.cpk
put "$OUT/DpFileList.bin" DpFileList.bin
echo "$SHA" > "$OUT/deployed.squads.sha"
echo "đã cài $(stat -c %s "$OUT/pes17league.cpk") byte, đội hình $SHA"

if [ -n "$RESTART" ]; then
  # chờ game (cả winedbg nếu game vừa crash) tắt hẳn thay vì chờ cố định: mở lại khi wine còn dọn dở từng làm game crash lúc khởi động (21/11)
  docker exec "$BENCH" sh -c "pkill -f 'PES2017[.]exe'; pkill -f 'wine[d]bg';
    for i in \$(seq 30); do pgrep -f 'PES2017[.]exe|wine[d]bg' >/dev/null || exit 0; sleep 0.5; done; pkill -9 -f 'PES2017[.]exe|wine[d]bg'" || true
  sleep 2
  docker exec -d "$BENCH" bash -lc "chmod 755 /defaults; su -s /bin/bash abc -c 'export HOME=/config DISPLAY=:1; exec /bench/run-pes17.sh' >/tmp/pes-out.log 2>&1"
  echo "đã mở lại game"
fi
