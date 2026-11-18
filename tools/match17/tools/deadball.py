#!/usr/bin/env python3
"""Gỡ bóng chết khi tay ảo đang gán cho một đội.

Phát bóng / ném biên / phạt / phạt góc của đội có tay cầm: game đứng chờ người bấm,
góc phải trên hiện bảng hướng dẫn (Position Shift / Display Help Guides / Camera).
Thấy bảng đó trên sân → bấm A (chuyền ngắn). Menu đỏ "Select Kicker" → B.
Màn khác (bảng số, menu hết hiệp, video, Pause) → không bấm gì.

Chạy: .venv/bin/python -u tools/deadball.py   (Ctrl+C / kill để dừng)
"""
from __future__ import annotations

import json, sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import loop

LOG = loop.SPIKE / "events" / "deadball.jsonl"


help_guide = loop.help_guide


def main():
    last = 0.0
    red_n = 0
    with LOG.open("a") as fh:
        while True:
            try:
                im = loop.grab()
            except Exception as e:
                loop.emit(fh, {"event": "error", "what": str(e)})
                time.sleep(3)
                continue
            screen = loop.classify(im)
            now = time.monotonic()
            if screen == "setpiece":
                red_n += 1
                if red_n >= 2 and now - last > 4:
                    loop.pad("B w:0.5 B")
                    last = now
                    red_n = 0
                    loop.emit(fh, {"event": "unstick", "t": time.strftime("%H:%M:%S")})
            else:
                red_n = 0
                if screen == "hud" and help_guide(im) and now - last > 2.5:
                    loop.pad("A w:0.3")
                    last = now
                    loop.emit(fh, {"event": "deadball_a", "t": time.strftime("%H:%M:%S")})
            time.sleep(1.0)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        pass
