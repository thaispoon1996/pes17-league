#!/usr/bin/env python3
"""Bộ chạy trận PES 2017 (pes17-bench) theo hợp đồng ScriptMatchRunner (giống tools/match của PES6), chạy trong container web.

  pesmatch17.py run CONTAINER SQUADS_JSON HOME_ID AWAY_ID [home|away|both]
    SQUADS_JSON = ảnh chụp đội hình vòng (Pes17RoundService), HOME_ID/AWAY_ID = mã đội PES17 (ClubPesMappings.PesClubIndex).
    1. giữ $DATA/pes17-deploy/match.lock (sync.sh không khởi động lại game khi đang đá)
    2. đội hình + sơ đồ + vai + chiến thuật của 2 đội trận này đã có trong game (squads.json đã cài) → không cài lại, không khởi động lại game;
       khác → tools/pes17db/deploy.sh --squads SQUADS_JSON --force --restart (PES17 chỉ đọc dữ liệu lúc khởi động)
    3. đi menu tới Select Team (chỉ bấm A khi không nhận ra màn chọn đội / Kickoff), chọn giải + CLB bằng mẫu ảnh tên (m17nav)
    4. Match Time: trận có người 10 phút, trận BOT 5 phút. Trận BOT: Y bật Coach Mode (máy đá cả 2 bên) → Kickoff. Trận có người (home|away): dừng ở màn Kickoff ("ready"),
       PESMATCH_WAIT_GO=1 thì chờ dòng "go" ở stdin rồi bấm Kickoff; người chơi (Selkies, link stream trên web) tự chọn bên và đá.
       PESMATCH_WARM=1 (web dựng sẵn, PO 21/11): như WAIT_GO, thêm "sync FILE" (HLV sửa đội hình lúc chờ, web đã gom) và "go FILE"
       (đội hình lúc bấm Sẵn sàng): 2 đội khác game → cài lại + khởi động lại game (sync: ngay trong lúc HLV còn chỉnh).
    5. theo dõi trận bằng OCR (tools/m17ocr.py, RapidOCR): HUD → tỷ số + phút từng bàn; bảng số / menu nghỉ → A;
       tỷ số cuối = số to ở menu hết trận. Khi đang đá không bấm gì (Coach Mode: máy tự đá cả bóng chết).
    6. về lại màn chọn đội cho trận sau; xoá match.lock.
  Màn chọn tay cầm (HOME | giữa | AWAY; Wine thấy 4 tay cầm ảo selkies nhờ libSDL2, 18/11): mỗi trận đi qua 1 lần (B từ danh sách giải),
    xếp P1 (tay cầm index 0: máy + người đội nhà) ở HOME; "both" (giao hữu/2 người): P2 (index 1, link #player2) sang AWAY,
    máy chọn đội khách bằng tay cầm P2; trận khác: không ai đứng AWAY. "both": bấm Kickoff xong, thấy sân → stage pitch (web cấp vé).
  Sự kiện stdout (JSON mỗi dòng): stage boot|menu|teams|ready|loading, kickoff, goal{side,half,minute,wall,slot:null}, end{home,away},
  error{reason}. Bàn thắng in ngay trước "end", đã khớp tỷ số cuối (ghi kết quả nguồn Memory đòi khớp).
  ratings{home:[{shirt,rating}],away:[...]} đọc từ bảng Player Rankings ở menu hết trận (11 người đầu mỗi bên).
  PESMATCH_LOGO=FILE (trận giải, PO 22/11): logo giải góc phải trên trang chơi Selkies từ lúc giao bóng tới hết trận (overlay(), tools/bench/pes-overlay.js).
  Bảng tỉ số riêng (PO 23/11, tools/board17.py): phủ bảng tỉ số game ở trang chơi + bản ghi/phát trực tiếp, logo giải nằm trong bảng
  (khi có bảng thì không đặt logo góc phải). PESMATCH_BOARD=0 tắt (chỉ để thử).
  PESMATCH_GRAPHICS=DIR + PESMATCH_COMPETITION=GIẢI (PO 23/11): cài gói đồ hoạ menu của giải trước khi vào trận (ensure_compe(), tools/pes17db/compe.py);
  DIR/house.cpk = đồ hoạ chung mọi trận (khung đội hình, chuyển cảnh chiếu lại, nền menu; ensure_house(), tools/pes17db/house.py).
  AFK (trận 2 người): xem AFK_STALL / Presence (tools/afk17.py). Chưa có: hiệp phụ/penalty (PESMATCH_EXTRA_TIME), người ghi bàn.
  Ghi hình PESMATCH_REC=FILE.mkv (+ PESMATCH_LIVE=FILE.m3u8 xem trực tiếp), 23/11: chỉ khi $DATA/pes17-deploy/record-mode = bot|all
  (mặc định tắt, tools/rec17.py). Ghi từ lúc bấm Kickoff tới hết trận; sự kiện recording{path} / live{path}; khi ghi, "wall" = giây trong video.
"""
from __future__ import annotations

import hashlib, json, os, queue, re, subprocess, sys, threading, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
import m17nav as nav   # noqa: E402
import loop            # noqa: E402
import m17ocr as ocr   # noqa: E402
import rec17           # noqa: E402
import board17         # noqa: E402
from PIL import Image, ImageChops, ImageStat   # noqa: E402

REPO = ROOT.parents[1]
DATA = Path(os.environ.get("PES17_DATA") or (Path(os.environ["PES17_STORE"]).parent if os.environ.get("PES17_STORE") else Path.home() / "pes17league-data"))
OUT = DATA / "pes17-deploy"
LOCK = Path(os.environ.get("PESMATCH_LOCK") or OUT / "match.lock")   # web truyền đường dẫn để tự xoá khi dừng bộ chạy ngang (PO 22/11)
WAIT_GO = os.environ.get("PESMATCH_WAIT_GO") == "1"
WARM = os.environ.get("PESMATCH_WARM") == "1"
BENCH = os.environ.get("PES17_BENCH", "pes17-bench")
REC = os.environ.get("PESMATCH_REC")     # web luôn truyền; chỉ ghi khi record-mode cho phép (rec17.wanted)
LOGO = os.environ.get("PESMATCH_LOGO")   # logo giải của trận (web, PO 22/11) → góc phải trên màn hình chơi, từ lúc giao bóng tới hết trận
GRAPHICS = os.environ.get("PESMATCH_GRAPHICS")         # $DATA/graphics của web (PO 23/11): gói menu theo giải compe/{giải}.cpk (trang /admin/do-hoa)
COMPETITION = os.environ.get("PESMATCH_COMPETITION")   # mã giải của trận (Guid dạng N); trống = giao hữu / không thuộc giải
OVERLAY = "/bench/overlay"                # trong pes17-bench; trang Selkies đọc ở pes/overlay.json (tools/bench/pes-overlay.js)
BOOT_TIMEOUT = 300        # từ lúc mở game tới màn chọn đội
NO_PITCH_TIMEOUT = 600    # quá 10 phút không thấy HUD trận → treo
HUMAN_PREMATCH_TIMEOUT = 1800
HUMAN_HALFTIME_TIMEOUT = 600   # trận có người: nghỉ giữa hiệp quá 10 phút → máy bấm 2nd Half
GOAL_LAG = 1.5   # mốc bàn = lần cuối thấy tỷ số cũ + 1,5 s (1 nhịp đọc HUD ~3 s; Match.old_seen)
# Luật AFK (giống PES6, PO 17/11; PES17 18/11): trận 2 người, chưa giao bóng hoặc đồng hồ đứng quá 5 phút (không tính nghỉ giữa hiệp)
# → bên không bấm gì ≥ 4 phút là vắng: đúng 1 bên → error afk_home|afk_away (bên đó xử thua 0-3), cả hai/không rõ → afk (huỷ).
AFK_STALL = int(os.environ.get("PESMATCH_AFK_STALL", 300))   # biến môi trường chỉ để thử
AFK_IDLE = int(os.environ.get("PESMATCH_AFK_IDLE", 240))
CMDS: "queue.Queue[tuple[str, str | None]]" = queue.Queue()   # ("go"|"sync", file đội hình|None) từ stdin
T0 = time.monotonic()


def emit(**kw) -> None:
    print(json.dumps(kw, ensure_ascii=False), flush=True)


def log(msg: str) -> None:
    print(f"{time.strftime('%T')} {msg}", file=sys.stderr, flush=True)


class NavError(Exception):
    pass


# ---------- nhận màn ----------

_KICK = None


def kickoff_screen(im: Image.Image) -> bool:
    """Thanh UNIFORM | STADIUM | KICKOFF | GAME PLAN | GENERAL SETTINGS dưới đáy màn trước trận."""
    global _KICK
    if _KICK is None:
        _KICK = Image.open(ROOT / "templates" / "screens" / "kickoff_bar.png").convert("L")
    bar = im.crop((70, 612, 1215, 648)).convert("L")
    return ImageStat.Stat(ImageChops.difference(bar, _KICK)).mean[0] < 18


def header(im: Image.Image, side: str) -> tuple[str | None, str | None, float]:
    """(loại 'leagues'|'teams', mã, điểm) của thẻ tên đang chọn bên `side`."""
    m = nav.mask(nav.crop(im, side))
    out = (None, None, 0.0)
    for kind in ("leagues", "teams"):
        k, s = nav.best(m, nav.templates(kind))
        if s > out[2]:
            out = (kind, k, s)
    return out if out[2] >= 0.85 else (None, None, out[2])


def is_ask(im: Image.Image) -> bool:
    return loop.mid_white(im) > 8000 and loop.green_pitch(im) < 500


# ---------- đi menu ----------

def to_team_select(both: bool = False, away: bool = False, fresh: bool = False) -> None:
    """Tới danh sách giải bên nhà của màn Select Team, qua màn chọn tay cầm đúng 1 lần (xếp P1/P2, xem arrange_pads).
    Chỉ bấm A khi không nhận ra màn (tiêu đề, hộp thoại mạng, menu chính).
    fresh: game vừa khởi động → không thể đang đá/ở menu hết trận, bỏ OCR HUD/menu (~2,5 s mỗi lần bấm). Game crash lúc khởi động → mở lại (tối đa 2 lần)."""
    end = time.monotonic() + BOOT_TIMEOUT
    stage, arranged, backs, restarts, dead = None, False, 0, 0, 0
    while time.monotonic() < end:
        im = nav.shot("m17_nav")
        kind, key, _ = header(im, "home")
        if kind == "leagues":
            if arranged or backs >= 2:   # B 2 lần không ra màn tay cầm → thôi (giữ cách cũ)
                if not arranged:
                    log("không thấy màn chọn tay cầm, bỏ qua bước xếp tay cầm")
                return
            backs += 1
            nav.pad("B w:3")
            continue
        if kind is None and pad_screen(im) is not None:
            arrange_pads(both, away)
            arranged = True
            continue
        if kind == "teams" or kickoff_screen(im) or header(im, "away")[0]:
            nav.pad("B w:2")
            continue
        if not fresh:
            if ocr.hud(im) or ocr.stats_screen(im):
                raise NavError("game đang đá (thấy HUD/bảng số)")
            if ocr.menu(im):   # menu hết trận còn lại từ trận trước: Select Team
                nav.pad("Right w:0.6 A w:3")
                if is_ask(nav.shot("m17_nav")):
                    nav.pad("Left w:0.4 A w:3")
                continue
        dead = dead + 1 if (st := game_state()) == "dead" else 0
        if st == "crash" or dead >= 2:
            if restarts >= 2:
                raise NavError(f"game_crash: PES2017 lỗi khi khởi động ({st})")
            restarts += 1
            log(f"game {st} → mở lại game (lần {restarts})")
            restart_game()
            end, fresh, dead = time.monotonic() + BOOT_TIMEOUT, True, 0
            continue
        if stage != "menu":
            stage = "menu"
            emit(event="stage", stage="menu")
        nav.pad("A w:3")
    raise NavError("nav_timeout: không tới được màn chọn đội")


PAD_COLS = ((270, 375), (590, 695), (910, 1010))   # HOME | giữa | AWAY
PAD_ROWS = (178, 295, 410, 525)                     # tâm y 4 dòng tay cầm


def _dark(g: Image.Image, box) -> int:
    return g.crop(box).point(lambda v: 255 if v < 50 else 0).histogram()[255]


def pad_screen(im: Image.Image) -> list[int | None] | None:
    """Màn chọn tay cầm: cột của từng dòng (0 HOME, 1 giữa, 2 AWAY, None không có tay cầm). Không phải màn này → None.
    Tay cầm đang cắm = hình đen (~2900 điểm tối/ô), ô trống xám (0)."""
    g = im.convert("L")
    rows = []
    for y in PAD_ROWS:
        dark = [_dark(g, (x0, y - 35, x1, y + 35)) for x0, x1 in PAD_COLS]
        on = [i for i, d in enumerate(dark) if d > 2000]
        if len(on) > 1 or any(200 < d <= 2000 for d in dark):
            return None
        rows.append(on[0] if on else None)
    if rows[0] is None or not any("ersonal" in t for t, _, _ in ocr.det(im, (80, 655, 800, 692))):
        return None
    return rows


def arrange_pads(both: bool, away: bool = False) -> None:
    """P1 (index 0) đứng HOME; both: P2 (index 1) đứng AWAY; trận khác: không ai đứng AWAY (P1 chọn cả 2 đội). Rồi A.
    away (PO 21/11: người chơi đá đội khách — trước đây P1 luôn HOME nên cầm nhầm đội nhà): xem arrange_away."""
    if away and not both:
        return arrange_away()
    for _ in range(5):
        rows = pad_screen(nav.shot("m17_pads"))
        if rows is None:
            raise NavError("nav_pads: mất màn chọn tay cầm")
        home, away_n = rows.count(0), rows.count(2)
        if home >= 1 and away_n == (1 if both else 0):
            log(f"tay cầm: {rows}")
            nav.pad("A w:3")
            return
        if both and away_n == 0:
            nav.pad("Right w:1", 1)
        elif away_n > (1 if both else 0):
            nav.pad("Left w:1", 1)
        if home == 0:
            nav.pad("Left w:1", 0)
    raise NavError(f"nav_pads: không xếp được tay cầm {rows}")


# Dò 21/11: dòng 0 = P1 (slot 0, mỗi lần bấm nhảy hẳn sang HOME/AWAY); dòng 1 đi theo phím của slot 0 từng nấc
# ([2,2] -L→ [0,1] -L→ [0,0] -R→ [2,1]); dòng 2 = slot 1; dòng 3 = slot 2.
ROW_SLOT = {0: 0, 2: 1, 3: 2}


def arrange_away() -> None:
    """P1 đứng AWAY, không tay cầm nào đứng HOME (máy đá đội nhà), tay cầm khác về giữa. Rồi A."""
    rows: list | None = None
    for _ in range(8):
        rows = pad_screen(nav.shot("m17_pads"))
        if rows is None:
            raise NavError("nav_pads: mất màn chọn tay cầm")
        if rows[0] == 2 and all(c in (None, 1) for c in rows[1:]):
            log(f"tay cầm (P1 đội khách): {rows}")
            nav.pad("A w:3")
            return
        if rows[1] == 2:            # dòng 1 kẹt AWAY: 2 lần Left kéo cả P1 lẫn dòng 1 về HOME, vòng sau Right → [2, 1]
            nav.pad("Left w:2.5 Left w:2.5", 0)
            continue
        if rows[0] == 0 and rows[1] == 1:   # lúc mới vào màn: Left → [0, 0], vòng sau Right → [2, 1] (Right ngay → [2, 2] mất thêm 2 lần bấm)
            nav.pad("Left w:2.5", 0)
            continue
        if rows[0] != 2 or rows[1] == 0:
            nav.pad("Right w:2.5", 0)
            continue
        for row, col in enumerate(rows[2:], 2):
            if col in (0, 2) and row in ROW_SLOT:
                nav.pad(("Right" if col == 0 else "Left") + " w:2.5", ROW_SLOT[row])
    raise NavError(f"nav_pads: không xếp được tay cầm P1 đội khách {rows}")


def pick(home: int, away: int, both: bool = False) -> None:
    emit(event="stage", stage="teams")
    nav.SLOT["away"] = 1 if both else 0
    for side, club in (("home", home), ("away", away)):
        r = nav.pick_side(side, club)
        if not r.get("ok"):
            raise NavError(f"nav_pick_{side}: {r.get('why')}")
    end = time.monotonic() + 30
    while time.monotonic() < end:
        if kickoff_screen(nav.shot("m17_nav")):
            return
        time.sleep(1.5)
    raise NavError("nav_kickoff: không thấy màn Kickoff")


MATCH_TIME = 10            # trận có người: phút cả trận trong game = 5 phút/hiệp (PO 18/11)
BOT_MATCH_TIME = 5         # trận BOT vs BOT: 2,5 phút/hiệp cho nhanh (PO 19/11)
MT_VALUE = (780, 345, 970, 372)   # ô giá trị dòng "Match Time" trong GENERAL SETTINGS
ML_VALUE = (780, 316, 970, 343)   # ô giá trị dòng "Match Level" (ngay trên Match Time)
LEVELS = ["Beginner", "Amateur", "Regular", "Professional", "Top Player", "Superstar"]   # độ khó máy, thứ tự trong game
QUALITIES = {"low": 0, "medium": 1, "high": 2}   # settings.dat byte 24
SETTINGS_DAT = "/config/.wine/drive_c/users/abc/Documents/KONAMI/Pro Evolution Soccer 2017/settings.dat"


def game_settings() -> dict:
    """Cài đặt game do web ghi ($DATA/pes17-deploy/game-settings.json, trang /cai-dat, PO 22/11): level, quality. Lỗi/thiếu → {}."""
    try:
        d = json.loads((OUT / "game-settings.json").read_text())
        return d if isinstance(d, dict) else {}
    except (OSError, ValueError):
        return {}


def _norm(t: str) -> str:
    return re.sub(r"[^a-z]", "", t.lower())


def match_level(want: str) -> None:
    """Đang trong GENERAL SETTINGS, con trỏ ở dòng Match Level: bấm Trái/Phải tới độ khó `want` (OCR). Lỗi thì ghi log, không chặn trận."""
    target, order = _norm(want), [_norm(x) for x in LEVELS]
    key, last, flipped = None, None, False
    for _ in range(14):
        v = _norm(ocr.rec(nav.shot("m17_ml"), ML_VALUE)[0])
        if v == target:
            return
        if key is None:
            key = "Left" if v in order and order.index(target) < order.index(v) else "Right"
        elif v == last and not flipped:   # bấm mà không đổi (chạm đầu/cuối danh sách) → đổi chiều 1 lần
            key, flipped = ("Left" if key == "Right" else "Right"), True
        last = v
        nav.pad(f"{key} w:0.4")
    log(f"không chỉnh được độ khó {want} (đang '{last}')")


def match_time(want: int = MATCH_TIME, level: str | None = None) -> None:
    """Màn Kickoff (con trỏ ở KICKOFF): GENERAL SETTINGS → (Match Level = level) → Match Time = want → về lại KICKOFF.
    Game chưa có thư mục save → cài đặt có thể mất khi khởi động lại game, nên kiểm mỗi trận (~6 s nếu đã đúng)."""
    nav.pad("Right w:0.6 Right w:0.6 A w:2.5")
    if level:
        match_level(level)
    nav.pad("Down w:0.8")
    try:
        for _ in range(30):
            t = ocr.rec(nav.shot("m17_mt"), MT_VALUE)[0]
            m = re.match(r"\s*(\d{1,2})", t)
            if not m:
                raise NavError(f"nav_match_time: không đọc được '{t}'")
            v = int(m[1])
            if v == want:
                return
            nav.pad(" ".join([("Right" if v < want else "Left") + " w:0.3"] * abs(want - v)) + " w:0.3")   # bấm đủ nấc 1 lượt rồi đọc lại
        raise NavError("nav_match_time: không chỉnh được")
    finally:
        nav.pad("B w:2.5 Left w:0.6 Left w:0.6")


def coach_icon(im: Image.Image) -> int:
    """Biểu tượng HLV xanh trên danh sách đội nhà (màn Kickoff): tắt ~0, bật ~243 (spike 17/11)."""
    n = 0
    for y in range(240, 360, 2):
        for x in range(330, 460, 2):
            r, g, b = im.getpixel((x, y))
            if b > 180 and r < 90 and 90 < g < 190:
                n += 1
    return n


def coach_mode(on: bool) -> None:
    for _ in range(4):
        if (coach_icon(nav.shot("m17_coach")) > 100) == on:
            return
        nav.pad("Y w:1.5")
    raise NavError(f"nav_coach: không {'bật' if on else 'tắt'} được Coach Mode")


# ---------- trong trận ----------

class Presence:
    """Lần bấm cuối của từng bên (tools/afk17.py trong container: tay cầm index 0/1 + bàn phím = đội nhà)."""

    def __init__(self):
        subprocess.run(["docker", "cp", str(ROOT / "tools" / "afk17.py"), "pes17-bench:/tmp/afk17.py"], check=True, capture_output=True)
        self.p = subprocess.Popen(["docker", "exec", "-i", "-u", "abc", "pes17-bench", "/lsiopy/bin/python3", "/tmp/afk17.py"],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
        self.idle: dict = {"home": None, "away": None}
        self.at = time.monotonic()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self) -> None:
        for line in self.p.stdout:
            try:
                self.idle, self.at = json.loads(line), time.monotonic()
            except ValueError:
                pass

    def absent(self) -> list[str]:
        """Bên vắng: chưa bấm lần nào hoặc không bấm ≥ AFK_IDLE giây. Bộ đo chết (không nhịp 30 s) → không rõ = cả hai."""
        age = time.monotonic() - self.at
        if age > 30:
            return ["home", "away"]
        return [k for k in ("home", "away") if self.idle.get(k) is None or self.idle[k] + age >= AFK_IDLE]

    def close(self) -> None:
        self.p.kill()   # đóng stdin của docker exec -i → tiến trình trong container cũng thoát (socket selkies tự nhả)
        subprocess.run(["docker", "exec", "pes17-bench", "pkill", "-f", "/tmp/afk17[.]py"], capture_output=True)


def afk_reason(pres: "Presence") -> str:
    absent = pres.absent()
    emit(event="afk", absent=absent)
    return f"afk_{absent[0]}" if len(absent) == 1 else "afk"


class Match:
    """Theo dõi 1 trận bằng OCR (m17ocr): HUD "1-0" + đồng hồ → đang đá, bàn thắng (phút = đồng hồ game);
    bảng số Half Time/Time Up → A; menu nghỉ (đồng hồ 45:00) → A (ô 2nd Half chọn sẵn); menu hết trận → tỷ số cuối.
    Replay/cận cảnh tự qua, máy không bấm gì khi đang đá (cả trận BOT lẫn trận có người)."""

    def __init__(self, human: str | None, presence: "Presence | None" = None, t0: float | None = None,
                 board: "board17.Board | None" = None):
        self.human = human
        self.board = board         # bảng tỉ số riêng: hiện khi thấy HUD game, ẩn khi HUD ẩn 2 nhịp liền (ăn mừng, chiếu lại, menu)
        self.t0 = t0               # mốc 0 của bản ghi hình (rec17.start): có → "wall" = giây trong video, không thì tính từ giao bóng
        self.presence = presence   # trận 2 người: đo AFK
        self.halftime = None
        self.half = 0
        self.kick = None
        self.hud = (0, 0)
        self.pending, self.seen = None, 0
        self.goals: list[dict] = []    # {side, half, minute, wall}
        self.minute = 0
        # Lần cuối HUD còn hiện tỷ số cũ: bóng vào lưới là game cắt sang ăn mừng + chiếu lại (HUD ẩn ~75 s), HUD hiện tỷ số mới
        # khi giao bóng lại → mốc bàn = lần cuối thấy tỷ số cũ + nửa nhịp đọc (đo 23/11 trên bản ghi: bàn ~287 s, thấy tỷ số mới 379 s).
        self.old_seen: float | None = None

    def wall(self) -> float:
        base = self.t0 or self.kick
        return round(time.monotonic() - base, 1) if base else 0.0

    def on_hud(self, h: dict) -> None:
        if h["minute"] is not None:
            self.minute = h["minute"]
        cur = (h["home"], h["away"])
        if cur == self.hud:
            self.pending, self.seen = None, 0
            self.old_seen = self.wall()
            return
        if any(c - o > 2 for c, o in zip(cur, self.hud)):   # +3 bàn trong 1 nhịp = đọc sai (vd "1-0" → "10-0"), bỏ hẳn
            return
        if cur != self.pending:
            self.pending, self.seen = cur, 1
            return
        self.seen += 1
        # bàn thật lên từng bàn một → 2 nhịp trùng là đủ; nhảy >1 (OCR đọc "1-0" thành "10-0") hoặc tụt (sửa đọc sai) → cần 4 nhịp
        step = [c - o for c, o in zip(cur, self.hud)]
        normal = sorted(step) == [0, 1]
        if self.seen < (2 if normal else 4):
            return
        for i, side in enumerate(("home", "away")):
            for _ in range(step[i]):
                minute = max(1, min(self.minute or (45 * self.half + 45), 90 if self.half else 45))
                wall = min(self.wall(), self.old_seen + GOAL_LAG) if self.old_seen is not None else self.wall()
                self.goals.append({"side": side, "half": self.half, "minute": minute, "wall": wall})
                log(f"bàn {side} phút {minute} → {cur}")
            for _ in range(-step[i]):   # sửa đọc sai: bỏ bàn ghi sau cùng của bên đó
                ks = [k for k, g in enumerate(self.goals) if g["side"] == side]
                if not ks:
                    break
                j = ks[-1]
                log(f"bỏ bàn {side} phút {self.goals.pop(j)['minute']} → {cur}")
        self.hud, self.pending, self.seen = cur, None, 0
        self.old_seen = self.wall()

    def shown(self) -> tuple[int, int]:
        """Tỉ số cho bảng riêng: HUD vừa đọc thấy hơn 1 bàn (chưa đủ nhịp để ghi bàn) thì hiện luôn — bảng game đã đổi,
        bảng riêng không được chậm hơn; nhảy lạ (đọc sai "10-0") thì giữ tỉ số đã chốt."""
        if self.pending and sorted(c - o for c, o in zip(self.pending, self.hud)) == [0, 1]:
            return self.pending
        return self.hud

    def final_goals(self, hg: int, ag: int) -> list[dict]:
        """Bàn HUD bắt được, cắt/bù cho khớp tỷ số cuối (bàn bù: phút 90)."""
        out = []
        for side, n in (("home", hg), ("away", ag)):
            seen = [g for g in self.goals if g["side"] == side][:n]
            out += seen + [{"side": side, "half": 1, "minute": 90, "wall": self.wall()}] * (n - len(seen))
        return sorted(out, key=lambda g: (g["half"], g["minute"], g["wall"]))

    def run(self) -> tuple[int, int]:
        last_play = progress = time.monotonic()   # progress: lần cuối đồng hồ game nhích (hoặc đang nghỉ giữa hiệp)
        last_press, last_min, misses = 0.0, None, 0
        while True:
            try:
                im = nav.shot("m17_live")
            except Exception as e:
                log(f"chụp màn lỗi: {e}")
                time.sleep(2)
                continue
            now = time.monotonic()
            h = ocr.hud(im)
            if self.presence and now - progress > AFK_STALL:
                raise NavError(afk_reason(self.presence))
            if not h and self.board and (misses := misses + 1) >= 2:
                self.board.hide()
            if h:
                last_play = now
                self.halftime = None   # hết nghỉ giữa hiệp
                if h["minute"] is not None and h["minute"] != last_min:
                    last_min, progress = h["minute"], now
                if self.kick is None:
                    self.kick = now
                    emit(event="kickoff")
                    if LOGO and not self.board:
                        overlay(LOGO)
                self.on_hud(h)
                misses = 0
                if self.board:
                    self.board.show(self.shown(), self.minute)
            elif now - last_play > (NO_PITCH_TIMEOUT if not self.human
                                    else HUMAN_PREMATCH_TIMEOUT if not self.kick or self.halftime else NO_PITCH_TIMEOUT):
                raise NavError("no_progress: quá lâu không thấy HUD trận")
            elif now - last_press > 3:
                m = ocr.menu(im)
                if m and self.kick:
                    # chỉ tin tiêu đề: menu tạm dừng (người chơi bấm Start) cũng có tỷ số + đồng hồ
                    t = m["title"]
                    full = "Up" in t or "ull" in t
                    if not full and "Hal" not in t:
                        time.sleep(2.5)
                        continue
                    if m != getattr(self, "_last_menu", None):
                        log(f"menu {m}")
                    self._last_menu = m
                    if full:
                        im.save(OUT / "last-fulltime.png")
                        if (m["home"], m["away"]) != self.hud:
                            log(f"tỷ số menu {m['home']}-{m['away']} khác HUD {self.hud}")
                        return m["home"], m["away"]
                    if self.human and self.halftime is None:   # trận có người: để PO thay người/chiến thuật rồi tự bấm 2nd Half
                        self.halftime = now
                        log("nghỉ giữa hiệp: chờ người chơi bấm 2nd Half")
                    if not self.human or now - self.halftime > HUMAN_HALFTIME_TIMEOUT:
                        nav.pad("A w:1")   # 2nd Half
                    self.half, self.minute = 1, 45
                    last_press = now
                    last_play = progress = now   # đang ở menu nghỉ: không tính là treo / AFK
                elif ocr.stats_screen(im):
                    nav.pad("A w:1")   # Half Time / Time Up
                    last_press = now
            time.sleep(2.5)


def back_to_team_select() -> None:
    nav.pad("Right w:0.6 A w:3")
    if is_ask(nav.shot("m17_nav")):
        nav.pad("Left w:0.4 A w:3")


# ---------- chính ----------

def game_state() -> str:
    """ok | crash (Wine "Program Error": winedbg, gặp 21/11 ngay sau khi khởi động lại) | dead (không còn PES2017.exe)."""
    # winedbg cũ hơn game = sót lại từ lần crash trước (game đã mở lại bằng tay) → không tính
    r = subprocess.run(["docker", "exec", BENCH, "sh", "-c",
                        "g=$(pgrep -n -f 'PES2017[.]exe') || { echo dead; exit; }; ge=$(ps -o etimes= -p $g);"
                        " for d in $(pgrep -f 'wine[d]bg'); do [ $(ps -o etimes= -p $d) -le $ge ] && { echo crash; exit; }; done; echo ok"],
                       capture_output=True, text=True)
    return r.stdout.strip() or "ok"


def restart_game() -> None:
    """Tắt hẳn game (cả winedbg) rồi mở lại, như deploy.sh --restart."""
    subprocess.run(["docker", "exec", BENCH, "sh", "-c",
                    "pkill -f 'PES2017[.]exe'; pkill -f 'wine[d]bg'; for i in $(seq 30); do pgrep -f 'PES2017[.]exe|wine[d]bg' >/dev/null || exit 0; sleep 0.5; done;"
                    " pkill -9 -f 'PES2017[.]exe|wine[d]bg'; true"], capture_output=True)
    time.sleep(2)
    subprocess.run(["docker", "exec", "-d", BENCH, "bash", "-lc",
                    "su -s /bin/bash abc -c 'export HOME=/config DISPLAY=:1; exec /bench/run-pes17.sh' >/tmp/pes-out.log 2>&1"], capture_output=True)
    emit(event="stage", stage="boot")
    time.sleep(25)


def deploy(squads: str) -> None:
    r = subprocess.run([str(REPO / "tools" / "pes17db" / "deploy.sh"), "--squads", squads, "--force", "--restart"],
                       capture_output=True, text=True, env={**os.environ, "PES17_DATA": str(DATA)})
    log(r.stdout.strip().splitlines()[-1] if r.stdout.strip() else r.stderr[-300:])
    if r.returncode != 0:
        raise NavError(f"deploy_failed: {r.stderr.strip()[-300:]}")
    if "đã mở lại game" in r.stdout:
        emit(event="stage", stage="boot")
        time.sleep(25)


def _sha(doc: dict) -> str:   # như deploy.sh
    return hashlib.sha256(json.dumps(doc, sort_keys=True).encode()).hexdigest()[:16]


def teams_part(doc: dict, home: int, away: int) -> str:
    """Phần của 2 đội trận này trong squads.json: cầu thủ/số áo/thứ tự + sơ đồ + vai + chiến thuật."""
    ks = (str(home), str(away))
    part = {k: doc.get(k) for k in ks}
    for extra in ("_formations", "_jobs", "_plan"):
        part[extra] = {k: (doc.get(extra) or {}).get(k) for k in ks}
    return json.dumps(part, sort_keys=True)


def game_has(squads: str, home: int, away: int) -> bool:
    """Game đang chạy và gói đã cài (squads.json khớp deployed.squads.sha) có đúng phần 2 đội của `squads`."""
    try:
        cur = json.loads((OUT / "squads.json").read_text())
        if _sha(cur) != (OUT / "deployed.squads.sha").read_text().strip():
            return False
        want = json.loads(Path(squads).read_text())
    except (OSError, ValueError):
        return False
    if teams_part(cur, home, away) != teams_part(want, home, away):
        return False
    r = subprocess.run(["docker", "exec", BENCH, "pgrep", "-f", "PES2017.exe"], capture_output=True)
    return r.returncode == 0


def ensure_quality(want: str | None) -> bool:
    """Đồ hoạ (Thấp/Trung bình/Cao, PO 22/11): settings.dat byte 24 khác → ghi lại; trả True nếu cần mở lại game để áp dụng."""
    if want not in QUALITIES:
        return False
    v = QUALITIES[want]
    r = subprocess.run(["docker", "exec", BENCH, "python3", "-c",
                        "import sys;p=sys.argv[1];b=bytearray(open(p,'rb').read());v=int(sys.argv[2])\n"
                        "if len(b)<28 or b[:4]!=b'WECF': print('bad')\n"
                        "elif b[24]==v: print('same')\n"
                        "else: b[24]=v;open(p,'wb').write(b);print('changed')", SETTINGS_DAT, str(v)],
                       capture_output=True, text=True)
    out = r.stdout.strip()
    if out == "changed":
        log(f"đồ hoạ → {want}: mở lại game")
        return True
    if out != "same":
        log(f"không đổi được đồ hoạ: {out or r.stderr.strip()[-200:]}")
    return False


def ensure_compe() -> bool:
    """Gói đồ hoạ menu của giải (phương án D, PO 23/11; tools/pes17db/compe.py): giải có gói → cài, giải chưa có → gỡ gói đang cài;
    chưa giải nào có gói và máy không cài gói nào → không đụng. Trả True nếu đã đổi (game chỉ đọc gói lúc mở → mở lại game).
    Lỗi chỉ ghi log: đồ hoạ là phụ, trận vẫn đá."""
    if not GRAPHICS:
        return False
    try:
        d = Path(GRAPHICS) / "compe"
        if not any(d.glob("*.cpk")) and not (OUT / "compe.active").exists():
            return False
        cpk = d / f"{COMPETITION}.cpk" if COMPETITION else None
        target = str(cpk) if cpk and cpk.is_file() else "none"
        r = subprocess.run([sys.executable, str(REPO / "tools" / "pes17db" / "compe.py"), "install", target],
                           capture_output=True, text=True, timeout=300)
        if r.returncode:
            log(f"gói đồ hoạ giải lỗi: {(r.stderr or r.stdout).strip()[-200:]}")
            return False
        if json.loads(r.stdout).get("changed"):
            log(f"gói đồ hoạ giải → {Path(target).stem}: mở lại game")
            return True
    except Exception as e:
        log(f"gói đồ hoạ giải lỗi: {e}")
    return False


def ensure_house() -> bool:
    """Gói đồ hoạ chung mọi trận (PO 23/11; tools/pes17db/house.py, web /admin/do-hoa dựng $GRAPHICS/house.cpk): có gói → cài
    (khác gói đang cài), không có → gỡ. Trả True nếu đã đổi (mở lại game). Lỗi chỉ ghi log."""
    if not GRAPHICS:
        return False
    try:
        cpk = Path(GRAPHICS) / "house.cpk"
        if not cpk.is_file() and not (OUT / "house.active").exists():
            return False
        target = str(cpk) if cpk.is_file() else "none"
        r = subprocess.run([sys.executable, str(REPO / "tools" / "pes17db" / "house.py"), "install", target],
                           capture_output=True, text=True, timeout=300)
        if r.returncode:
            log(f"gói đồ hoạ chung lỗi: {(r.stderr or r.stdout).strip()[-200:]}")
            return False
        if json.loads(r.stdout).get("changed"):
            log("gói đồ hoạ chung đổi: mở lại game")
            return True
    except Exception as e:
        log(f"gói đồ hoạ chung lỗi: {e}")
    return False


def ensure_game(squads: str, home: int, away: int) -> bool:
    """Cài đội hình nếu 2 đội của trận khác game (khởi động lại game). Trả True nếu đã cài lại."""
    if game_has(squads, home, away):
        log("game đã có đội hình 2 đội → không cài lại")
        return False
    deploy(squads)
    return True


def read_cmds() -> None:
    for line in sys.stdin:
        w = line.split(maxsplit=1)
        if w and w[0] in ("go", "sync"):
            CMDS.put((w[0], w[1].strip() if len(w) > 1 else None))


def wait_go(squads: str, home: int, away: int, setup) -> bool:
    """Chờ lệnh go (nút Sẵn sàng trên web). "sync FILE"/"go FILE" có đội hình 2 đội khác game → cài lại, khởi động lại game,
    setup() về màn Kickoff (stage boot…ready) rồi chờ tiếp / vào trận. Chỉ cài lại khi đang thấy màn Kickoff (người chơi chưa tự vào trận).
    Người chơi tự bấm Kickoff bằng tay cầm (màn Kickoff mất 3 lần liên tiếp) → coi như đã vào trận. Trả True nếu người chơi tự vào."""
    gone, t0, go, latest = 0, time.monotonic(), False, squads
    while True:
        try:
            cmd, path = CMDS.get(timeout=2)
            go, latest = go or cmd == "go", path or latest
            if not CMDS.empty():
                continue   # lấy lệnh mới nhất rồi mới xử lý
        except queue.Empty:
            pass
        on_kick = kickoff_screen(nav.shot("m17_wait"))
        if on_kick and latest != squads:
            if not game_has(latest, home, away):
                log("đội hình 2 đội đổi → cài lại game")
                deploy(latest)
                setup(True)
                if not go:
                    emit(event="stage", stage="ready")
            squads, gone = latest, 0
            continue
        if go:
            return False
        gone = 0 if on_kick else gone + 1
        if gone >= 3:
            log("người chơi tự bấm vào trận")
            return True
        if time.monotonic() - t0 > HUMAN_PREMATCH_TIMEOUT:
            raise NavError("human_timeout: không bấm vào trận")


def overlay(logo: str | None) -> None:
    """Đặt (logo-<sha>.png + overlay.json {"logo": tên}) / xoá ({}) logo giải trên trang chơi. Lỗi thì bỏ qua: logo là phụ."""
    try:
        data, name = b"", ""
        if logo:
            data = Path(logo).read_bytes()
            name = f"logo-{hashlib.sha1(data).hexdigest()[:10]}{Path(logo).suffix.lower() or '.png'}"
        doc = json.dumps({"logo": name} if name else {})
        sh = (f"mkdir -p {OVERLAY} && cd {OVERLAY} && "
              + (f"cat > {name}.tmp && mv {name}.tmp {name} && chmod 644 {name} && find . -maxdepth 1 -name 'logo-*' ! -name {name} -delete && " if name else "")
              + f"printf '%s' '{doc}' > overlay.json.tmp && chmod 644 overlay.json.tmp && mv overlay.json.tmp overlay.json")
        subprocess.run(["docker", "exec", "-i", BENCH, "sh", "-c", sh], input=data, capture_output=True, timeout=20, check=True)
    except Exception as e:
        log(f"logo giải lỗi: {e}")


def make_board(home: int, away: int) -> "board17.Board | None":
    """Bảng tỉ số riêng của trận (mã 3 chữ từ Team.raw, logo giải PESMATCH_LOGO). Lỗi → None: trận vẫn đá, trang chơi thấy bảng gốc của game."""
    if os.environ.get("PESMATCH_BOARD") == "0":
        return None
    try:
        cs = board17.codes(DATA / "pesdb" / "Team.raw", [home, away])
        return board17.Board(BENCH, OVERLAY, OUT / "board", cs.get(home, ""), cs.get(away, ""), LOGO, log)
    except Exception as e:
        log(f"bảng tỉ số lỗi: {e}")
        return None


def grass(im: Image.Image) -> int:
    """Điểm màu cỏ (lưới 8 px, tối đa 5750) — chịu được bóng râm khán đài (loop.green_pitch thì không). Sân ~5600, menu < 400."""
    n = 0
    for y in range(220, 620, 8):
        for x in range(180, 1100, 8):
            r, g, b = im.getpixel((x, y))
            if g > 35 and g > r * 1.15 + 5 and g > b * 1.1 + 5:
                n += 1
    return n


def leave_match() -> None:
    """Đang trong trận: Start (nếu chưa dừng) → mục cuối "Top Menu" của Pause Menu → Yes → menu chính. Đo 18/11: Right x5 tới mục cuối, hộp hỏi mặc định No."""
    try:
        if "ause" not in ocr.rec(nav.shot("m17_leave"), ocr.MENU_TITLE)[0]:
            nav.pad("Start w:2")
        nav.pad(" ".join(["Right w:0.5"] * 5) + " A w:2")
        if is_ask(nav.shot("m17_leave")):
            nav.pad("Left w:0.5 A w:6")
        emit(event="stage", stage="left_match")
    except Exception as e:   # không chặn việc báo lỗi AFK
        log(f"không thoát được trận: {e}")


def wait_pitch() -> None:
    """Trận 2 người: sau Kickoff chờ thấy sân (phim chào sân/đứng chờ giao bóng) → stage pitch (web cấp vé + link cho 2 người)."""
    end, seen = time.monotonic() + 180, 0
    while time.monotonic() < end:
        im = nav.shot("m17_pitch")
        seen = seen + 1 if grass(im) > 3500 or ocr.hud(im) else 0
        if seen >= 2:
            emit(event="stage", stage="pitch")
            return
        time.sleep(2)
    raise NavError("nav_pitch: không thấy sân sau Kickoff")


def run(container: str, squads: str, home: int, away: int, human: str | None) -> int:
    if container != "pes17-bench":
        log(f"container {container}: bộ chạy PES17 chỉ dùng pes17-bench")
    OUT.mkdir(parents=True, exist_ok=True)
    LOCK.write_text(f"{os.getpid()} {home}-{away}\n")
    recording, t_rec = None, None
    try:
        emit(event="stage", stage="boot")
        overlay(None)   # bỏ logo sót của trận trước (bộ chạy bị dừng ngang)
        rec17.stop(f"pesrec-{container}")   # bản ghi sót của lần chạy bị dừng ngang → đóng file gọn
        if WAIT_GO or WARM:
            threading.Thread(target=read_cmds, daemon=True).start()
        gs = game_settings()
        gfx = ensure_quality(gs.get("quality"))   # ghi trước: cài đội hình (nếu có) cũng mở lại game
        gfx = ensure_house() or gfx               # đồ hoạ chung mọi trận (house.active)
        gfx = ensure_compe() or gfx               # trước ensure_game: deploy.sh ghi DpFileList theo compe.active + house.active
        restarted = ensure_game(squads, home, away)
        if gfx and not restarted:
            restart_game()
            restarted = True
        both = human == "both"
        level = gs.get("level") if human and not both and gs.get("level") in LEVELS else None   # độ khó chỉ có nghĩa khi người đá với máy

        def setup(fresh: bool) -> None:
            with nav.session():   # 1 kết nối tay cầm cho cả đoạn đi menu; đóng trước khi người chơi cầm tay cầm
                to_team_select(both, human == "away", fresh)
                pick(home, away, both)
                match_time(MATCH_TIME if human else BOT_MATCH_TIME, level)
                coach_mode(human is None)

        setup(restarted)
        self_started = False
        if human and not both:
            emit(event="stage", stage="ready")
            if WAIT_GO or WARM:
                self_started = wait_go(squads, home, away, setup)
        emit(event="stage", stage="loading")
        board = make_board(home, away)
        if REC and rec17.wanted(OUT, human):
            recording, t_rec = rec17.start(container, REC, os.environ.get("PESMATCH_LIVE"), emit,
                                           board.y if board else board17.window_y(BENCH), board.file if board else None)
        if not self_started:
            nav.pad("A w:2")   # Kickoff
        pres = Presence() if both else None
        try:
            if both:
                wait_pitch()
            m = Match(human, pres, t_rec, board)
            hg, ag = m.run()
        except NavError as e:
            if str(e).startswith("afk"):
                leave_match()   # huỷ/xử thua vì AFK: bỏ trận đang dở để trận sau vào được màn chọn đội
            raise
        finally:
            rec17.stop(recording)   # trước "end": web dừng bộ chạy ngay sau "end"
            recording = None
            if pres:
                pres.close()
            if board:
                board.close()
            elif LOGO:
                overlay(None)
        try:
            emit(event="ratings", **ocr.rankings(nav.shot("m17_ft")))
        except Exception as e:   # điểm cầu thủ là phụ: lỗi thì bỏ
            log(f"đọc điểm cầu thủ lỗi: {e}")
        for g in m.final_goals(hg, ag):
            emit(event="goal", slot=None, **g)
        emit(event="end", home=hg, away=ag)
        try:
            back_to_team_select()
        except Exception as e:   # trận đã ghi; lần sau to_team_select tự xử lý
            log(f"về màn chọn đội lỗi: {e}")
        return 0
    except NavError as e:
        emit(event="error", reason=str(e))
        return 1
    except Exception as e:
        emit(event="error", reason=f"{type(e).__name__}: {e}")
        return 1
    finally:
        rec17.stop(recording)   # lỗi trước khi vào trận (đã bắt đầu ghi)
        LOCK.unlink(missing_ok=True)


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) >= 5 and a[0] == "run":
        sys.exit(run(a[1], a[2], int(a[3]), int(a[4]), a[5] if len(a) > 5 else None))
    print(__doc__, file=sys.stderr)
    sys.exit(2)
