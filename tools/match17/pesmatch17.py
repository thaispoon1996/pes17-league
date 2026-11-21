#!/usr/bin/env python3
"""Bộ chạy trận PES 2017 (pes17-bench) theo hợp đồng ScriptMatchRunner (giống tools/match của PES6), chạy trong container web.

  pesmatch17.py run CONTAINER SQUADS_JSON HOME_ID AWAY_ID [home|away|both]
    SQUADS_JSON = ảnh chụp đội hình vòng (Pes17RoundService), HOME_ID/AWAY_ID = mã đội PES17 (ClubPesMappings.PesClubIndex).
    1. giữ $DATA/pes17-deploy/match.lock (sync.sh không khởi động lại game khi đang đá)
    2. tools/pes17db/deploy.sh --squads SQUADS_JSON --restart (đội hình giống bản đã cài thì không làm gì)
    3. đi menu tới Select Team (chỉ bấm A khi không nhận ra màn chọn đội / Kickoff), chọn giải + CLB bằng mẫu ảnh tên (m17nav)
    4. Match Time: trận có người 10 phút, trận BOT 5 phút. Trận BOT: Y bật Coach Mode (máy đá cả 2 bên) → Kickoff. Trận có người (home|away): dừng ở màn Kickoff ("ready"),
       PESMATCH_WAIT_GO=1 thì chờ dòng "go" ở stdin rồi bấm Kickoff; người chơi (Selkies, link stream trên web) tự chọn bên và đá.
    5. theo dõi trận bằng OCR (tools/m17ocr.py, RapidOCR): HUD → tỷ số + phút từng bàn; bảng số / menu nghỉ → A;
       tỷ số cuối = số to ở menu hết trận. Khi đang đá không bấm gì (Coach Mode: máy tự đá cả bóng chết).
    6. về lại màn chọn đội cho trận sau; xoá match.lock.
  Màn chọn tay cầm (HOME | giữa | AWAY; Wine thấy 4 tay cầm ảo selkies nhờ libSDL2, 18/11): mỗi trận đi qua 1 lần (B từ danh sách giải),
    xếp P1 (tay cầm index 0: máy + người đội nhà) ở HOME; "both" (giao hữu/2 người): P2 (index 1, link #player2) sang AWAY,
    máy chọn đội khách bằng tay cầm P2; trận khác: không ai đứng AWAY. "both": bấm Kickoff xong, thấy sân → stage pitch (web cấp vé).
  Sự kiện stdout (JSON mỗi dòng): stage boot|menu|teams|ready|loading, kickoff, goal{side,half,minute,wall,slot:null}, end{home,away},
  error{reason}. Bàn thắng in ngay trước "end", đã khớp tỷ số cuối (ghi kết quả nguồn Memory đòi khớp).
  ratings{home:[{shirt,rating}],away:[...]} đọc từ bảng Player Rankings ở menu hết trận (11 người đầu mỗi bên).
  AFK (trận 2 người): xem AFK_STALL / Presence (tools/afk17.py). Chưa có: ghi hình (PESMATCH_REC), hiệp phụ/penalty (PESMATCH_EXTRA_TIME), người ghi bàn.
"""
from __future__ import annotations

import json, os, re, subprocess, sys, threading, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
import m17nav as nav   # noqa: E402
import loop            # noqa: E402
import m17ocr as ocr   # noqa: E402
from PIL import Image, ImageChops, ImageStat   # noqa: E402

REPO = ROOT.parents[1]
DATA = Path(os.environ.get("PES17_DATA") or (Path(os.environ["PES17_STORE"]).parent if os.environ.get("PES17_STORE") else Path.home() / "pes17league-data"))
OUT = DATA / "pes17-deploy"
LOCK = OUT / "match.lock"
WAIT_GO = os.environ.get("PESMATCH_WAIT_GO") == "1"
BOOT_TIMEOUT = 300        # từ lúc mở game tới màn chọn đội
NO_PITCH_TIMEOUT = 600    # quá 10 phút không thấy HUD trận → treo
HUMAN_PREMATCH_TIMEOUT = 1800
HUMAN_HALFTIME_TIMEOUT = 600   # trận có người: nghỉ giữa hiệp quá 10 phút → máy bấm 2nd Half
# Luật AFK (giống PES6, PO 17/11; PES17 18/11): trận 2 người, chưa giao bóng hoặc đồng hồ đứng quá 5 phút (không tính nghỉ giữa hiệp)
# → bên không bấm gì ≥ 4 phút là vắng: đúng 1 bên → error afk_home|afk_away (bên đó xử thua 0-3), cả hai/không rõ → afk (huỷ).
AFK_STALL = int(os.environ.get("PESMATCH_AFK_STALL", 300))   # biến môi trường chỉ để thử
AFK_IDLE = int(os.environ.get("PESMATCH_AFK_IDLE", 240))
GO = threading.Event()
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

def to_team_select(both: bool = False, away: bool = False) -> None:
    """Tới danh sách giải bên nhà của màn Select Team, qua màn chọn tay cầm đúng 1 lần (xếp P1/P2, xem arrange_pads).
    Chỉ bấm A khi không nhận ra màn (tiêu đề, hộp thoại mạng, menu chính)."""
    end = time.monotonic() + BOOT_TIMEOUT
    stage, arranged, backs = None, False, 0
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
        if kind == "teams" or header(im, "away")[0] or kickoff_screen(im):
            nav.pad("B w:2")
            continue
        if ocr.hud(im) or ocr.stats_screen(im):
            raise NavError("game đang đá (thấy HUD/bảng số)")
        if ocr.menu(im):   # menu hết trận còn lại từ trận trước: Select Team
            nav.pad("Right w:0.6 A w:3")
            if is_ask(nav.shot("m17_nav")):
                nav.pad("Left w:0.4 A w:3")
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


def match_time(want: int = MATCH_TIME) -> None:
    """Màn Kickoff (con trỏ ở KICKOFF): GENERAL SETTINGS → Match Time = want → về lại KICKOFF.
    Game chưa có thư mục save → cài đặt có thể mất khi khởi động lại game, nên kiểm mỗi trận (~6 s nếu đã đúng)."""
    nav.pad("Right w:0.6 Right w:0.6 A w:2.5 Down w:0.8")
    try:
        for _ in range(30):
            t = ocr.rec(nav.shot("m17_mt"), MT_VALUE)[0]
            m = re.match(r"\s*(\d{1,2})", t)
            if not m:
                raise NavError(f"nav_match_time: không đọc được '{t}'")
            v = int(m[1])
            if v == want:
                return
            nav.pad(("Right" if v < want else "Left") + " w:0.6")
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

    def __init__(self, human: str | None, presence: "Presence | None" = None):
        self.human = human
        self.presence = presence   # trận 2 người: đo AFK
        self.halftime = None
        self.half = 0
        self.kick = None
        self.hud = (0, 0)
        self.pending, self.seen = None, 0
        self.goals: list[dict] = []    # {side, half, minute, wall}
        self.minute = 0

    def wall(self) -> float:
        return round(time.monotonic() - self.kick, 1) if self.kick else 0.0

    def on_hud(self, h: dict) -> None:
        if h["minute"] is not None:
            self.minute = h["minute"]
        cur = (h["home"], h["away"])
        if cur == self.hud:
            self.pending, self.seen = None, 0
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
                self.goals.append({"side": side, "half": self.half, "minute": minute, "wall": self.wall()})
                log(f"bàn {side} phút {minute} → {cur}")
            for _ in range(-step[i]):   # sửa đọc sai: bỏ bàn ghi sau cùng của bên đó
                ks = [k for k, g in enumerate(self.goals) if g["side"] == side]
                if not ks:
                    break
                j = ks[-1]
                log(f"bỏ bàn {side} phút {self.goals.pop(j)['minute']} → {cur}")
        self.hud, self.pending, self.seen = cur, None, 0

    def final_goals(self, hg: int, ag: int) -> list[dict]:
        """Bàn HUD bắt được, cắt/bù cho khớp tỷ số cuối (bàn bù: phút 90)."""
        out = []
        for side, n in (("home", hg), ("away", ag)):
            seen = [g for g in self.goals if g["side"] == side][:n]
            out += seen + [{"side": side, "half": 1, "minute": 90, "wall": self.wall()}] * (n - len(seen))
        return sorted(out, key=lambda g: (g["half"], g["minute"], g["wall"]))

    def run(self) -> tuple[int, int]:
        last_play = progress = time.monotonic()   # progress: lần cuối đồng hồ game nhích (hoặc đang nghỉ giữa hiệp)
        last_press, last_min = 0.0, None
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
            if h:
                last_play = now
                self.halftime = None   # hết nghỉ giữa hiệp
                if h["minute"] is not None and h["minute"] != last_min:
                    last_min, progress = h["minute"], now
                if self.kick is None:
                    self.kick = now
                    emit(event="kickoff")
                self.on_hud(h)
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

def deploy(squads: str) -> None:
    r = subprocess.run([str(REPO / "tools" / "pes17db" / "deploy.sh"), "--squads", squads, "--restart"],
                       capture_output=True, text=True, env={**os.environ, "PES17_DATA": str(DATA)})
    log(r.stdout.strip().splitlines()[-1] if r.stdout.strip() else r.stderr[-300:])
    if r.returncode != 0:
        raise NavError(f"deploy_failed: {r.stderr.strip()[-300:]}")
    if "đã mở lại game" in r.stdout:
        emit(event="stage", stage="boot")
        time.sleep(25)


def read_go() -> None:
    for line in sys.stdin:
        if line.strip().startswith("go"):
            GO.set()
            return


def wait_go() -> bool:
    """Chờ lệnh go (nút "Vào trận" trên web). Người chơi tự bấm Kickoff bằng tay cầm (qua Selkies) thì màn Kickoff mất
    → coi như đã vào trận, không bấm A nữa. Trả True nếu người chơi tự vào."""
    gone, t0 = 0, time.monotonic()
    while not GO.wait(2):
        gone = 0 if kickoff_screen(nav.shot("m17_wait")) else gone + 1
        if gone >= 3:
            log("người chơi tự bấm vào trận")
            return True
        if time.monotonic() - t0 > HUMAN_PREMATCH_TIMEOUT:
            raise NavError("human_timeout: không bấm vào trận")
    return False


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
    try:
        emit(event="stage", stage="boot")
        deploy(squads)
        both = human == "both"
        to_team_select(both)
        pick(home, away, both)
        match_time(MATCH_TIME if human else BOT_MATCH_TIME)
        coach_mode(human is None)
        self_started = False
        if human and not both:
            emit(event="stage", stage="ready")
            if WAIT_GO:
                threading.Thread(target=read_go, daemon=True).start()
                self_started = wait_go()
        emit(event="stage", stage="loading")
        if not self_started:
            nav.pad("A w:2")   # Kickoff
        pres = Presence() if both else None
        try:
            if both:
                wait_pitch()
            m = Match(human, pres)
            hg, ag = m.run()
        except NavError as e:
            if str(e).startswith("afk"):
                leave_match()   # huỷ/xử thua vì AFK: bỏ trận đang dở để trận sau vào được màn chọn đội
            raise
        finally:
            if pres:
                pres.close()
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
        LOCK.unlink(missing_ok=True)


if __name__ == "__main__":
    a = sys.argv[1:]
    if len(a) >= 5 and a[0] == "run":
        sys.exit(run(a[1], a[2], int(a[3]), int(a[4]), a[5] if len(a) > 5 else None))
    print(__doc__, file=sys.stderr)
    sys.exit(2)
