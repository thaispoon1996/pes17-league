#!/bin/bash
# Chạy web vận hành (Pes17League.Web) nền trong container pes17league-web (cùng image với tools/cli.sh: SDK + docker CLI + python3 + ImageMagick).
# Cổng 3200 trên Tailscale (100.66.228.1) + 127.0.0.1. Mật khẩu admin trong $PES17_DATA/web.env (tự sinh lần đầu).
# Dùng: tools/web.sh        — build + (khởi động lại) web
#       tools/web.sh logs   — xem log
#       PES17_WEB_AUTH=off tools/web.sh — bỏ đăng nhập (PO trải nghiệm thử); =on bật lại. Lưu trong $PES17_DATA/web-auth, lần sau tools/web.sh giữ nguyên
#       tools/web.sh stop   — dừng
R=$(cd "$(dirname "$0")/.." && pwd)
DATA=${PES17_DATA:-$HOME/pes17league-data}
case "$1" in
  logs) exec docker logs --tail 100 -f pes17league-web ;;
  stop) exec docker rm -f pes17league-web ;;
esac
mkdir -p "$DATA"
[ -n "$PES17_WEB_AUTH" ] && echo "$PES17_WEB_AUTH" > "$DATA/web-auth"
AUTH=$(cat "$DATA/web-auth" 2>/dev/null || echo on)
[ -f "$DATA/web.env" ] || { echo "PES17_WEB_PASSWORD=$(head -c 12 /dev/urandom | base64 | tr -d '/+=')" > "$DATA/web.env"; chmod 600 "$DATA/web.env"; }
# Khóa viết bài (DeepSeek). Không in ra. Thiếu khóa thì web vẫn đăng bài mẫu từ số liệu trận.
NEWS_KEY="${DEEPSEEK_API_KEY:-}"
if [ -z "$NEWS_KEY" ] && [ -f "$HOME/projects/knowledge-hub/.env" ]; then
  NEWS_KEY=$(sed -n 's/^DEEPSEEK_API_KEY=//p' "$HOME/projects/knowledge-hub/.env" | head -n 1 | tr -d '"' | tr -d "'")
fi
news_env=()
[ -n "$NEWS_KEY" ] && news_env+=(-e "DEEPSEEK_API_KEY=${NEWS_KEY}")
out=$(docker build -q -t pes17league-cli:local "$R/tools/cli" 2>&1) || { echo "$out" >&2; exit 1; }
out=$(docker build -q -t pes17league-rec:local "$R/tools/rec" 2>&1) || { echo "$out" >&2; exit 1; }   # ffmpeg ghi hình + highlight (C7)
docker rm -f pes17league-web >/dev/null 2>&1
# Giờ hiển thị trên web (nhật ký việc, giờ dự kiến, sổ cái): giờ Việt Nam (PO 07/11); DB vẫn lưu UTC.
usb=(); [ -d /data/pes17league ] && usb=(-v /data/pes17league:/data/pes17league)   # video ghi hình nằm ở ổ USB /data (symlink từ $DATA/recordings, PO 13/11)
docker run -d --name pes17league-web --restart unless-stopped --env-file "$DATA/web.env" "${news_env[@]}" \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/nuget -e TZ=Asia/Ho_Chi_Minh \
  -e PES17_DB="${PES17_DB:-Host=127.0.0.1;Port=5437;Username=postgres;Database=pes17league}" -e PES17_STORE="$DATA/store" \
  -e PES17_WEB_AUTH="$AUTH" -e Match__Script=/src/tools/match17/pesmatch17.py -e ASPNETCORE_URLS="http://${PES17_WEB_HOST:-100.66.228.1}:3217;http://127.0.0.1:3217" \
  -v /var/run/docker.sock:/var/run/docker.sock -v "$R":/src -v pes17league-nuget:/nuget -v "$DATA":"$DATA" -v "$HOME":"$HOME":ro "${usb[@]}" \
  --network host -w /src pes17league-cli:local \
  sh -c 'dotnet publish -v q -c Release -o /out src/Hosts/Pes17League.Web >/dev/null && chown -R '"$(id -u):$(id -g)"' /src '"$DATA"' && cd /out && exec dotnet Pes17League.Web.dll' >/dev/null
echo "Web: http://${PES17_WEB_HOST:-100.66.228.1}:3217 (đang build, ~30s). Đăng nhập: $AUTH. Mật khẩu: xem $DATA/web.env"
