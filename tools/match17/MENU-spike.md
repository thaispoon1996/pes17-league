# Bản đồ menu PES17 HANO V6

Stream: https://100.66.228.1:3117  
Pad ảo: `tools/pad.sh "A Down …"` (Selkies ws://127.0.0.1:8082 — **ổn định hơn xdotool**)

## Chuỗi Exhibition đã chứng minh (17/11 ~01:39 UTC)
| Bước | Màn | Pad | Ảnh |
|------|-----|-----|-----|
| 1 | Title PRESS X | `A` | go_00 / 32_wait |
| 2 | FDDN532 | `A` | go_01 |
| 3 | HOME | (tự tới / thêm `A`) | go_03 |
| 4 | MATCH → PLAY MATCH popup (VERSUS / Exhibition) | `Right`/`A` hoặc từ HOME | go_04 |
| 5 | Exhibition Match | `Down` `A` | go_05 |
| 6 | Live Update? | `A`=Yes / `Right A`=No | go_07 |
| 7 | Select Team HOME/AWAY | `A` league → team → `A` | go_08…10 |
| 8 | Load tips → intro lineup | chờ | go_load_* |
| 9 | **Trong trận** | — | **go_load_15** LEI–ARS 0–0 @1'46 |

Settings Exhibition (khi vào được): Players / Match Level / Time 10 min / Injuries.

## Lưu ý
- UCL tile → **New** = giải đấu (group stage), **không** phải 1 trận — tránh nhầm với Exhibition.
- Live Update Yes có thể đụng patch; nên thử `No` khi ổn định.
- `pad17.py`: A=0 Confirm, B=1 Back, D-pad 12–15.
