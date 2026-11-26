#!/usr/bin/env python3
"""Bảng tỉ số riêng (PO 23/11: "có") — phủ lên bảng tỉ số của game ở góc trên trái, giao diện xanh "Pro Pitch Broadcast".

Bộ chạy trận (pesmatch17 Match.run) gọi Board.show(tỉ số, phút) mỗi nhịp đọc HUD (~3 s), hide() khi HUD ẩn (ăn mừng, chiếu lại, menu nghỉ):
  - trang chơi Selkies: /bench/overlay/board.png + overlay.json {"board": {"v", "box": [x, y, w, h] theo màn hình thật 1280x720}}
    (tools/bench/pes-overlay.js đặt ảnh lên khung game; không có bảng → {"board": null}).
  - bản ghi + phát trực tiếp: $DATA/pes17-deploy/board/board.png — ffmpeg (tools/rec17.py) chồng ảnh, đọc lại file ~4 lần/giây
    (-f image2 -loop 1 -re, đo 23/11: trễ < 1 s). Ẩn = ảnh trong suốt cùng cỡ (đổi cỡ giữa chừng thì ffmpeg dựng lại bộ lọc).
OCR của bộ chạy chụp thẳng màn X (xwd) → không thấy bảng này.
Vị trí: HUD game (khung cũ shot.sh) x 62–374, y 64–99 → bảng 360x52 phủ x 48–408, y 32–84 tính trong khung game (bỏ viền xám 1px
Openbox); màn hình thật thêm Y cửa sổ (0 không viền, 23 có thanh tiêu đề); bản ghi đã cắt + co khung game về 1280x720 (rec17.frame).
Mã 3 chữ (ARS, AVL…) đọc từ Team.raw của game (byte 0x2EC mỗi đội), như HUD gốc.

  board17.py demo RA.png [logo.png]   — vẽ thử vài trạng thái ghép 1 ảnh
"""
from __future__ import annotations

import os, struct, subprocess, sys, time
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / "fonts" / "BarlowCondensed-ExtraBold.ttf"
W, H = 360, 52
BOX = (48, 32)            # góc trên trái trong khung game (đã bỏ viền 1px)
K = 3                     # vẽ to gấp 3 rồi thu nhỏ: góc bo, chữ mịn
FLASH = 8.0               # giây tô xanh tên đội vừa ghi bàn
BG, SURF, SURF2, LINE = (15, 19, 29, 255), (28, 31, 42, 255), (38, 42, 53, 255), (44, 48, 59, 255)
GREEN, TEXT, DARK = (0, 255, 135, 255), (222, 225, 247, 255), (15, 19, 29, 255)
TEAM_SIZE, SHORT_AT = 1400, 0x2EC


def codes(team_raw: Path, ids: list[int]) -> dict[int, str]:
    """Mã 3 chữ của đội (Team.raw giải nén của pesdb). Lỗi → {} (bảng dùng 3 chữ đầu tên… hoặc trống)."""
    out: dict[int, str] = {}
    try:
        raw = team_raw.read_bytes()
        want = set(ids)
        for i in range(0, len(raw) - TEAM_SIZE + 1, TEAM_SIZE):
            tid = struct.unpack_from("<I", raw, i + 8)[0]
            if tid in want:
                s = raw[i + SHORT_AT:i + SHORT_AT + 8].split(b"\0", 1)[0].decode("utf-8", "replace").strip()
                if s:
                    out[tid] = s.upper()[:4]
    except OSError:
        pass
    return out


def _font(px: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT), px * K)


def _center(d: ImageDraw.ImageDraw, box: tuple[int, int, int, int], text: str, font, fill) -> None:
    x0, y0, x1, y1 = box
    l, t, r, b = d.textbbox((0, 0), text, font=font)
    d.text(((x0 + x1) / 2 - (l + r) / 2, (y0 + y1) / 2 - (t + b) / 2), text, font=font, fill=fill)


def render(home: str, away: str, hs: int, as_: int, minute: int | None, logo: Image.Image | None = None,
           flash: str | None = None) -> Image.Image:
    """Ảnh RGBA W×H. flash = "home" | "away": ô tên đội vừa ghi bàn nền xanh."""
    im = Image.new("RGBA", (W * K, H * K), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = lambda *v: tuple(int(x * K) for x in v)
    d.rounded_rectangle(s(0, 0, W - 1, H - 1), radius=10 * K, fill=BG, outline=LINE, width=K)
    # ô logo giải (không có logo → vạch xanh)
    d.rounded_rectangle(s(1, 1, 52, H - 2), radius=9 * K, fill=SURF)   # Pillow 9.4 (container web) chưa có corners= → vuông mép phải
    d.rectangle(s(40, 1, 52, H - 2), fill=SURF)
    if logo is not None:
        lg = logo.convert("RGBA")
        lg.thumbnail((40 * K, 40 * K), Image.LANCZOS)
        im.alpha_composite(lg, (int((27 - lg.width / K / 2) * K), int((26 - lg.height / K / 2) * K)))
    else:
        d.rounded_rectangle(s(24, 14, 29, 38), radius=2 * K, fill=GREEN)
    name = _font(30)
    for side, (x0, x1), txt in (("home", (52, 132), home), ("away", (222, 302), away)):
        if flash == side:
            d.rectangle(s(x0, 1, x1, H - 2), fill=GREEN)
        _center(d, s(x0, 0, x1, H), txt, name, DARK if flash == side else TEXT)
    # tỉ số: viên xanh
    d.rounded_rectangle(s(136, 7, 218, H - 8), radius=7 * K, fill=GREEN)
    num = _font(32)
    _center(d, s(136, 3, 168, H - 3), str(hs), num, DARK)
    _center(d, s(168, 3, 186, H - 3), "–", _font(24), DARK)
    _center(d, s(186, 3, 218, H - 3), str(as_), num, DARK)
    # phút
    d.rounded_rectangle(s(302, 1, W - 2, H - 2), radius=9 * K, fill=SURF2)
    d.rectangle(s(302, 1, 314, H - 2), fill=SURF2)
    d.line(s(302, 1, 302, H - 2), fill=LINE, width=K)
    _center(d, s(302, 0, W - 1, H), f"{minute}'" if minute is not None else "", _font(28), GREEN)
    return im.resize((W, H), Image.LANCZOS)


def blank() -> Image.Image:
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def geometry(bench: str) -> tuple[int, int, int, int]:
    """(rộng, cao màn X, Y, cao cửa sổ game theo điểm ảnh thật: Y 0 = không viền, 23 = có thanh tiêu đề Openbox; cao 0 = không thấy).
    Lỗi → (1280, 720, 0, 0). Đo 26/11: 720p → cửa sổ 1284x700; 1080p → 1924x1060 (dải đen dưới luôn 20px).
    Màn 1080p (PO 26/11): mọi box trang chơi vẫn theo hệ 1280x720 → dùng window_y (đã co về hệ 1280); bản ghi dùng điểm ảnh thật."""
    try:
        out = subprocess.run(["docker", "exec", "-u", "abc", "-e", "DISPLAY=:1", bench, "sh", "-c",
                              "xwininfo -root | awk '/Width:/{w=$2} /Height:/{h=$2} END{print w+0, h+0}'; "
                              "xwininfo -name 'Pro Evolution Soccer 2017' 2>/dev/null | awk '/Absolute upper-left Y/{y=$4} /Height:/{h=$2} END{print y+0, h+0}'"],
                             capture_output=True, text=True, timeout=10).stdout.split()
        w, h = int(out[0]) or 1280, int(out[1]) or 720
        return w, h, int(out[2]), int(out[3])
    except (OSError, subprocess.SubprocessError, ValueError, IndexError):
        return 1280, 720, 0, 0


def window_y(bench: str) -> int:
    """Y cửa sổ game theo hệ 1280x720 (box trang chơi)."""
    w, _, y, _ = geometry(bench)
    return round(y * 1280 / w)


def game_vscale(g: tuple[int, int, int, int]) -> float:
    """Chiều dọc vùng game theo hệ 1280 so với 700 của khung 720p (1080p: 1060 → 706,7 → 1,0095); không thấy cửa sổ → 1."""
    w, _, _, h = g
    return h * 1280 / w / 700 if h > 0 else 1.0


def _save(im: Image.Image, path: Path) -> None:
    tmp = path.with_name(path.name + ".tmp.png")
    im.save(tmp, optimize=False, compress_level=1)
    os.chmod(tmp, 0o644)
    tmp.replace(path)


class Board:
    """Trạng thái bảng của 1 trận. Ghi file chỉ khi hình đổi. Lỗi ghi → log, trận vẫn đá (bảng là phụ)."""

    def __init__(self, bench: str, overlay_dir: str, rec_dir: Path, home: str, away: str, logo: str | None, log=print):
        self.bench, self.overlay_dir, self.log = bench, overlay_dir, log
        self.home, self.away = home or "NHÀ", away or "KHÁCH"
        self.logo = None
        if logo:
            try:
                self.logo = Image.open(logo)
                self.logo.load()
            except Exception as e:   # svg / hỏng → bảng không logo
                log(f"bảng tỉ số: không đọc được logo giải ({e})")
        rec_dir.mkdir(parents=True, exist_ok=True)
        self.file = rec_dir / "board.png"
        self.screen = geometry(bench)   # (rộng, cao, Y thật) cho bản ghi
        self.y = round(self.screen[2] * 1280 / self.screen[0])   # hệ 1280 cho box
        self.v = 0
        self.key: tuple | None = None
        self.score = (0, 0)
        self.flash_side, self.flash_until = None, 0.0
        _save(blank(), self.file)   # có sẵn trước khi ffmpeg mở
        self._selkies(None)

    def show(self, score: tuple[int, int], minute: int | None) -> None:
        now = time.monotonic()
        if score != self.score:
            gained = [s for s, a, b in zip(("home", "away"), score, self.score) if a > b]
            if gained:
                self.flash_side, self.flash_until = gained[-1], now + FLASH
            self.score = score
        flash = self.flash_side if now < self.flash_until else None
        self._put(("on", score, minute, flash), lambda: render(self.home, self.away, *score, minute, self.logo, flash))

    def hide(self) -> None:
        self._put(("off",), None)

    def close(self) -> None:
        try:
            _save(blank(), self.file)
        except OSError:
            pass
        self._selkies(None)

    def _put(self, key: tuple, draw) -> None:
        if key == self.key:
            return
        self.key = key
        try:
            im = draw() if draw else None
            _save(im or blank(), self.file)
            self._selkies(im)
        except Exception as e:
            self.log(f"bảng tỉ số lỗi: {e}")

    def _selkies(self, im: Image.Image | None) -> None:
        """board.png + overlay.json cho trang chơi (box theo màn hình thật: khung game bắt đầu ở (1, Y+1))."""
        import io, json
        data = b""
        if im is not None:
            buf = io.BytesIO()
            im.save(buf, "PNG", compress_level=1)
            data = buf.getvalue()
            self.v += 1
        doc = json.dumps({"board": {"v": self.v, "box": [BOX[0] + 1, BOX[1] + self.y + 1, W, H]}} if im is not None else {"board": None})
        d = self.overlay_dir
        sh = (f"mkdir -p {d} && cd {d} && "
              + ("cat > board.png.tmp && chmod 644 board.png.tmp && mv board.png.tmp board.png && " if data else "")
              + f"printf '%s' '{doc}' > overlay.json.tmp && chmod 644 overlay.json.tmp && mv overlay.json.tmp overlay.json")
        try:
            subprocess.run(["docker", "exec", "-i", self.bench, "sh", "-c", sh], input=data, capture_output=True, timeout=10, check=True)
        except (OSError, subprocess.SubprocessError) as e:
            self.log(f"bảng tỉ số (trang chơi) lỗi: {e}")


def demo(out: str, logo: str | None) -> None:
    lg = Image.open(logo) if logo else None
    rows = [render("ARS", "BOU", 0, 0, 1, lg), render("ARS", "BOU", 2, 1, 67, lg, "home"), render("MCI", "LIV", 10, 9, 90, None, "away")]
    sheet = Image.new("RGBA", (W + 40, (H + 20) * len(rows) + 20), (40, 120, 60, 255))
    for i, r in enumerate(rows):
        sheet.alpha_composite(r, (20, 20 + i * (H + 20)))
    sheet.save(out)


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) >= 2 and a[0] == "demo":
        demo(a[1], a[2] if len(a) > 2 else None)
    else:
        print(__doc__, file=sys.stderr)
        sys.exit(2)
