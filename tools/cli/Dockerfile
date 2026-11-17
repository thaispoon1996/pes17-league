# SDK .NET + docker CLI + python3 + ImageMagick: CLI gọi tools/match/pesmatch.py (docker exec vào container trận, so ảnh màn hình).
FROM mcr.microsoft.com/dotnet/sdk:8.0
RUN apt-get update && apt-get install -y --no-install-recommends python3 imagemagick && rm -rf /var/lib/apt/lists/*
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker
