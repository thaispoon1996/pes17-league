#!/bin/bash
# Chạy Pes17League.Cli trong container (SDK + docker CLI + python3 + ImageMagick), có docker.sock → điều khiển container trận pesmatch-*.
# Biến: PES17_DB (connection string, mặc định Postgres dev 127.0.0.1:5434), PES17_DATA (thư mục kho OF, mặc định ~/pes17league-data).
# Ví dụ: tools/cli.sh setup-alpha <file-option-PES17> 10 0 1 5 8 9 10 12 16 ; tools/cli.sh play-bots ; tools/cli.sh status
R=$(cd "$(dirname "$0")/.." && pwd)
DATA=${PES17_DATA:-$HOME/pes17league-data}
mkdir -p "$DATA"
out=$(docker build -q -t pes17league-cli:local "$R/tools/cli" 2>&1) || { echo "$out" >&2; exit 1; }
ARGS=()
for a in "$@"; do [ -f "$a" ] && a=$(realpath "$a"); ARGS+=("$a"); done
usb=(); [ -d /data/pes17league ] && usb=(-v /data/pes17league:/data/pes17league)   # video ghi hình nằm ở ổ USB /data (symlink từ $DATA/recordings, PO 13/11)
docker run --rm -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/nuget \
  -e PES17_DB="${PES17_DB:-Host=127.0.0.1;Port=5437;Username=postgres;Database=pes17league}" -e PES17_STORE="$DATA/store" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$R":/src -v pes17league-nuget:/nuget -v "$DATA":"$DATA" -v "$HOME":"$HOME":ro "${usb[@]}" \
  --network host -w /src pes17league-cli:local \
  sh -c 'dotnet run -v q --project src/Hosts/Pes17League.Cli -- "$@"; rc=$?; chown -R '"$(id -u):$(id -g)"' /src '"$DATA"'; exit $rc' sh "${ARGS[@]}"
