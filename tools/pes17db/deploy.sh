#!/bin/bash
# Đội hình DB → game PES 2017 (không Sider, PO duyệt 18/11/2026):
#   1. Pes17League.Cli export-pes17-squads → squads.json   (hoặc --squads FILE: dùng file có sẵn, vd ảnh chụp vòng từ bộ chạy trận)
#   2. đội hình giống bản đã cài (deployed.squads.sha) → thôi, không đóng gói/khởi động lại (trừ --force)
#   3. pesdb.py apply (pesdb gốc HANO) → PlayerAssignment.bin (+ TacticsFormation.bin khi có _formations, Tactics.bin khi có _plan)
#   4. cpk.py build → pes17league.cpk (khuôn gói HANO 23, đọc từ game)
#   5. ghi vào download/ của game qua docker exec + DpFileList.bin = danh sách gốc HANO (order +1) + pes17league.cpk CUỐI, order 1
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
python3 - "$ORIG" "$OUT/DpFileList.bin" <<'E'
import struct, sys
o = open(sys.argv[1], "rb").read()
ents = [bytearray(o[i:i + 48]) for i in range(0, len(o), 48)]
ents = [e for e in ents if not e[16:].startswith(b"pes17league.cpk")]
for e in ents:
    struct.pack_into("<I", e, 4, struct.unpack_from("<I", e, 4)[0] + 1)
me = bytearray(48); struct.pack_into("<I", me, 4, 1); me[16:31] = b"pes17league.cpk"
open(sys.argv[2], "wb").write(b"".join(ents) + me)
print("DpFileList:", len(ents), "gói HANO + pes17league.cpk")
E
put() { docker exec -i -u abc "$BENCH" sh -c 'cat > "$1.new" && mv "$1.new" "$1"' sh "$DL/$2" < "$1"; }
put "$OUT/pes17league.cpk" pes17league.cpk
put "$OUT/DpFileList.bin" DpFileList.bin
echo "$SHA" > "$OUT/deployed.squads.sha"
echo "đã cài $(stat -c %s "$OUT/pes17league.cpk") byte, đội hình $SHA"

if [ -n "$RESTART" ]; then
  docker exec "$BENCH" pkill -f PES2017.exe || true
  sleep 5
  docker exec -d "$BENCH" bash -lc "su -s /bin/bash abc -c 'export HOME=/config DISPLAY=:1; exec /bench/run-pes17.sh' >/tmp/pes-out.log 2>&1"
  echo "đã mở lại game"
fi
