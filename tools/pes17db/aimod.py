#!/usr/bin/env python3
"""Trí khôn AI (PO 26/11): chỉnh tham số AI gốc của PES 2017 (common/match/ai/*.json trong dt18_win.cpk) — KHÔNG đụng chỉ số cầu thủ.

  aimod.py build <mode> <dt18_win.cpk> <thư-mục-ra>   → in các dòng "đường/trong/gói=file" cho cpk.py build (mode "goc" → không in gì)

Tệp gốc là JSON có chú thích (Shift-JIS); chỉ thay giá trị số của khoá đã định, giữ nguyên chú thích + mã hoá.
Tham số đội/cầu thủ áp cho mọi cầu thủ do máy điều khiển (cả đồng đội không cầm của người chơi);
cpuLevel.json (riêng đội máy theo cấp độ) ở Superstar đã cao nhất nên không chỉnh.
"""
import re
import subprocess
import sys
from pathlib import Path

H = Path(__file__).resolve().parent
A = "common/match/ai/"

# AI+ : pressing/bọc lót chặt và đoán trước xa hơn, chạy chỗ + dứt điểm quyết đoán hơn (mức vừa phải; chỉnh tiếp theo cảm nhận PO).
PRESETS: dict[str, dict[str, dict[str, float]]] = {
    "plus": {
        A + "team/defence/defence.json": {
            "predictionFrame": 50,           # 40: đoán trước vị trí bóng xa hơn
            "beginCutBaseDist": 25.0,        # 20: tiền đạo/tiền vệ lùi cắt đường chuyền sớm hơn
            "beginCutCurrentDist": 18.0,     # 15
            "matchUpDfXLimit": 12.0,         # 10: hậu vệ dâng ra áp sát xa hơn
            "matchUpMfXLimit": 12.0,         # 10
            "maxSandDist": 18.0,             # 14: kẹp 2 người từ xa hơn
            "maxWaitTime": 15.0,             # 30: bọc lót bớt chậm trễ
        },
        A + "team/defence/defenceCover.json": {
            "coverDist": 4.5,                # 5.5: bọc lót sát hơn
            "futureFrame": 15,               # 10
        },
        A + "team/defence/defenceMark.json": {
            "predictionFrame": 50,           # 40: kèm người đoán trước xa hơn
        },
        A + "team/offence/spaceRun.json": {
            "chanceScore": 85,               # 100: nhận ra cơ hội chạy chỗ dễ hơn → chạy chỗ nhiều hơn
            "chanceScore_good": 75,          # 90
        },
        A + "player/offence/ballplayerShoot.json": {
            "shootDecidePoint": 0.65,        # 0.7: dứt điểm quyết đoán hơn
        },
    },
}
MODES = ("goc", *PRESETS)


def patch(text: bytes, values: dict[str, float]) -> bytes:
    for key, v in values.items():
        pat = re.compile(rb'("' + re.escape(key.encode()) + rb'"\s*:\s*)(-?[0-9.]+)')
        hits = pat.findall(text)
        if len(hits) != 1:
            raise SystemExit(f"aimod: khoá {key} xuất hiện {len(hits)} lần")
        old = hits[0][1]
        new = (str(float(v)) if isinstance(v, float) or b"." in old else str(int(v))).encode()
        text = pat.sub(lambda m: m.group(1) + new, text)
    return text


def build(mode: str, cpk: Path, out: Path) -> list[str]:
    if mode not in MODES:
        raise SystemExit(f"aimod: mode {mode} không có ({', '.join(MODES)})")
    lines = []
    for path, values in PRESETS.get(mode, {}).items():
        dst = out / "ai" / path
        dst.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([sys.executable, str(H / "cpk.py"), "get", str(cpk), path, str(dst)], check=True, capture_output=True)
        dst.write_bytes(patch(dst.read_bytes(), values))
        lines.append(f"{path}={dst}")
    return lines


if __name__ == "__main__":
    if len(sys.argv) == 5 and sys.argv[1] == "build":
        print("\n".join(build(sys.argv[2], Path(sys.argv[3]), Path(sys.argv[4]))))
    else:
        print(__doc__)
        sys.exit(2)
