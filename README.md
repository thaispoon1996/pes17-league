# PES 17 League

Quản lý giải PES 2017 (HANO V6) online. Nền sao từ PES League (PES 6) ngày 17/11/2026 — repo, DB, web, container RIÊNG, không dùng chung gì với PES 6.

- `src/Modules/Clubs` — CLB, cầu thủ, đội hình, mùa giải, lịch, cúp, BXH, kinh tế, chuyển nhượng, kỷ luật, tin tức (nghiệp vụ không phụ thuộc game).
- `src/Modules/OptionFile` — **còn là định dạng PES 6** (`KONAMI-WIN32PES6OPT`), sẽ thay bằng module file option PES 17 (giai đoạn 3).
- `src/Hosts/Pes17League.Web` (Blazor, cổng 3217), `src/Hosts/Pes17League.Cli`.
- Máy trận PES 17 (giai đoạn 2): `tools/match17/` — chưa có; bản thử ở `~/pes6/pes17-web/spike-league`.
- `ui-mock/` — bản React AI Studio cũ (tham khảo giao diện; nhánh `react-mock`).

Hạ tầng riêng: Postgres dev `pes17league-pg-dev` (127.0.0.1:5437, DB `pes17league`), test `pes17league-pg-test` (5438), dữ liệu `~/pes17league-data`, video `/data/pes17league`, máy game `pes17-bench` (stream :3117).

```sh
tools/dotnet.sh build Pes17League.sln
tools/dotnet.sh test Pes17League.sln
PES17_TEST_DB='Host=127.0.0.1;Port=5438;Username=postgres;Database=postgres' tools/dotnet.sh test
```
