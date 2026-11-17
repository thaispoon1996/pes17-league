#!/bin/bash
# Chạy dotnet CLI trong container SDK 8.0 (máy không cài SDK). Chạy root (user thường lỗi mutex NuGet), xong trả quyền file.
# OF fixture: biến PES_OF_FIXTURES (thư mục trên host, chỉ đọc).
# Test DB: biến PES17_TEST_DB (connection string Postgres, ví dụ 127.0.0.1:5433) → chạy --network host.
R=$(cd "$(dirname "$0")/.." && pwd)
FX=${PES_OF_FIXTURES:+-v $PES_OF_FIXTURES:/fixtures:ro -e PES_OF_FIXTURES=/fixtures}
DB=${PES17_TEST_DB:+--network host -e PES17_TEST_DB}
docker run --rm -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 -e NUGET_PACKAGES=/nuget \
  -v "$R":/src -v pes17league-nuget:/nuget $FX $DB -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  sh -c 'dotnet "$@"; rc=$?; chown -R '"$(id -u):$(id -g)"' /src; exit $rc' sh "$@"
