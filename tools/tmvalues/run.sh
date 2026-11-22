#!/bin/bash
# Giá thật Transfermarkt (PO 22/11, phương án C): tải transfermarkt-datasets (cộng đồng cào sẵn, cập nhật hằng tuần) → xuất cầu thủ DB
# (mùa mới nhất có giá) → ghép (match.py) → THU_MUC/real-values.csv. Nhập: tools/cli.sh import-real-values THU_MUC/real-values.csv [--dry]
# Giá chỉ đổi từ mùa 2 (lúc bắt đầu mùa tính lại giá). Dùng: tools/tmvalues/run.sh THU_MUC
set -euo pipefail
D=${1:?THU_MUC}; mkdir -p "$D"
URL=https://pub-e682421888d945d684bcae8890b0ec20.r2.dev/data
curl -sSfL -m 120 -o "$D/players.csv.gz" "$URL/players.csv.gz"
PG=${PES17_PG_CONTAINER:-pes17league-pg-dev}
docker exec "$PG" psql -U postgres -d pes17league -c "copy (
  select p.\"Id\", p.\"Name\", p.\"Position\", c.\"Name\" club, (a.\"Values\"->>'AGE')::int age, (a.\"Values\"->>'HEIGHT')::int h,
         pp.\"Overall\", pp.\"BasePrice\", pp.\"Price\"
  from clubs.\"Players\" p left join clubs.\"Clubs\" c on c.\"Id\" = p.\"OriginClubId\"
  join clubs.\"PlayerPrices\" pp on pp.\"PlayerId\" = p.\"Id\"
   and pp.\"SeasonId\" = (select s.\"Id\" from clubs.\"Seasons\" s where exists (select 1 from clubs.\"PlayerPrices\" x where x.\"SeasonId\" = s.\"Id\") order by s.\"Number\" desc limit 1)
  join clubs.\"PlayerAttributes\" a on a.\"PlayerId\" = p.\"Id\" and a.\"SeasonId\" = pp.\"SeasonId\") to stdout csv header" > "$D/db.csv"
python3 -I "$(dirname "$0")/match.py" "$D"
wc -l "$D/real-values.csv"
