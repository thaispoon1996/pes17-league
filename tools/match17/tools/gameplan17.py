#!/usr/bin/env python3
"""Đổi đội hình trong GAME PLAN của PES 2017 bằng tay cầm (hướng B, PO 23/11): đội hình trận sửa trên web áp thẳng vào game
đang chờ ở màn Kickoff, khỏi cài lại + mở lại game (~2 phút).

Màn GAME PLAN → "Lineup/Edit Position" (đo 23/11, khung 1280x720 như m17nav.shot):
  - sân bên trái (x 86–406, y 164–532): con trỏ bàn tay (đốm trắng ~20x20) nhảy giữa 11 người theo Lên/Xuống/Trái/Phải;
  - danh sách dự bị bên phải (x 420–600, dòng 0 ở y≈182, cách 27): Phải từ người ngoài cùng bên phải sân → vào danh sách;
    bàn tay danh sách nhỏ hơn (~14x12) đứng ở x≈400;
  - ô dưới trái hiện "số áo + tên" người đang trỏ (đọc OCR → biết chắc là ai);
  - A ở người 1 → di chuyển → A ở người 2 = đổi chỗ 2 người (đá chính ↔ đá chính, đá chính ↔ dự bị).
Cách làm: dò đồ thị nhảy của 11 vị trí (DFS, mỗi cạnh 1 lần bấm) + đọc số áo từng ô → ghép vị trí với ô 0–10 của sơ đồ
(toạ độ _formations) → đổi lần lượt từng ô sai → đọc lại kiểm tra.

  gameplan17.py probe              — đang ở Lineup/Edit Position: in đồ thị + số áo từng vị trí (JSON)
"""
from __future__ import annotations

import itertools, json, re, sys, time, unicodedata
from difflib import SequenceMatcher
from collections import deque
from pathlib import Path

import numpy as np
import cv2
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import m17nav as nav   # noqa: E402
import m17ocr as ocr   # noqa: E402

PANEL = (150, 560, 420, 595)        # "14 Pau Torres" người đang trỏ
PANEL2 = (418, 560, 640, 595)       # khi đã chọn 1 người (A): ô thứ hai = người đang trỏ
LIST_X, ROW0, ROWH = 390, 182, 27
DIRS = ("Up", "Down", "Left", "Right")
STEP = 0.55
VISIBLE = 12                        # danh sách hiện 12 dòng = đúng 12 ghế dự bị (vạch ngăn dưới dòng 12, thử 23/11)
AWAY_DX = 576                       # khung đội khách = khung đội nhà dời sang phải 576 px
_DX, _SLOT = 0, 0                   # khung + tay cầm đang dùng (apply đặt theo side)


def _pad(keys: str) -> None:
    nav.pad(keys, _SLOT)


def _box(b: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    return (b[0] + _DX, b[1], b[2] + _DX, b[3])


class PlanError(Exception):
    pass


def hand(im: Image.Image, pitch_only: bool = False) -> tuple[str, object] | None:
    """Vị trí bàn tay: ("pitch", (x, y)) | ("list", dòng) | None. pitch_only: màn không có danh sách (Player Settings)."""
    a = cv2.cvtColor(np.asarray(im), cv2.COLOR_RGB2HSV)
    m = ((a[..., 2] > 235) & (a[..., 1] < 25)).astype(np.uint8)
    m[:, :80 + _DX] = 0
    m[:, 640 + _DX:] = 0
    m[:160] = 0
    m[545:] = 0
    n, _, st, _ = cv2.connectedComponentsWithStats(m)
    c = [st[i] for i in range(1, n) if st[i, 4] > 100 and 10 <= st[i, 2] <= 34 and 10 <= st[i, 3] <= 34]
    if c:
        x, y, w, h, _ = max(c, key=lambda s: s[4])
    else:   # đã chọn 1 người (A): con trỏ thành mũi tên đổi chỗ ⇆ = 2 đốm ~12x8 lệch (8, 6), ở ngay dưới chỗ bàn tay
        a2 = [s for s in (st[i] for i in range(1, n)) if 40 <= s[4] <= 70 and 9 <= s[2] <= 15 and 5 <= s[3] <= 11]
        pair = next(((s, t) for s in a2 for t in a2 if 5 <= t[0] - s[0] <= 11 and 3 <= t[1] - s[1] <= 9), None)
        if not pair:
            return None
        x, y = pair[0][0], pair[0][1] - 3
    if x >= LIST_X + _DX and not pitch_only:
        return ("list", int(round((y - ROW0) / ROWH)))
    return ("pitch", (int(x), int(y)))


def number(im: Image.Image, box=PANEL, roster: dict[int, str] | None = None) -> int | None:
    """Số áo ở đầu ô thông tin. OCR hay dính chữ cái đầu tên vào số ("10 I. Ndiaye" → "101I.Ndiaye"): số không có trong đội
    mà tên đọc được là người có số áo là phần đầu của số đó → lấy số áo của người đó."""
    t = ocr.rec(im, _box(box))[0] + " "
    m = re.match(r"\s*(\d{1,3})\D", t)
    if not m:
        return None
    v = int(m[1])
    if roster and v not in roster:
        g = guess(t[m.end(1) - 1:], roster) or guess(t[m.end(1):], roster)
        if g is not None and m[1].startswith(str(g)):
            return g
    return v


class Board:
    """Đồ thị nhảy giữa các vị trí trên sân của 1 đội (đang ở Lineup/Edit Position)."""

    def __init__(self) -> None:
        self.nodes: list[tuple[int, int]] = []          # toạ độ bàn tay từng vị trí trên sân
        self.shirt: dict[int, int | None] = {}         # vị trí → số áo
        self.edges: dict[tuple[int, str], object] = {}  # (vị trí, nút) → vị trí | ("list", dòng)
        self.at: object = None
        self.selected = False
        self.lst: dict[int, int | None] = {}            # dòng danh sách (tính cả phần đã cuộn) → số áo
        self.off = 0                                    # số dòng danh sách đã cuộn
        self.top = False                                # đã kéo danh sách lên đầu, dòng 0 = dòng đầu thật
        self.sure: set[int] = set()                     # vị trí đã đọc số áo thật (không phải đoán từ tên)
        self.im: Image.Image | None = None
        self.roster: dict[int, str] | None = None

    def _node(self, p: tuple[int, int]) -> int:
        for i, q in enumerate(self.nodes):
            if abs(q[0] - p[0]) <= 8 and abs(q[1] - p[1]) <= 8:
                return i
        self.nodes.append(p)
        return len(self.nodes) - 1

    def look(self) -> object:
        for _ in range(3):
            im = nav.shot("m17_gp")
            h = hand(im)
            if h:
                break
            time.sleep(0.4)
        else:
            raise PlanError("không thấy con trỏ bàn tay")
        if h[0] == "list":
            self.at = ("list", h[1] + self.off)
            self.lst[self.at[1]] = number(im, PANEL2 if self.selected else PANEL, self.roster)
        else:
            self.at = self._node(h[1])
            v = number(im, PANEL2 if self.selected else PANEL, self.roster)   # đọc thật mỗi lần trỏ qua → đè số đoán từ tên
            if v is not None or self.at not in self.shirt:
                self.shirt[self.at] = v
                self.sure.add(self.at)
        self.im = im
        return self.at

    def press(self, key: str, record: bool = True) -> object:
        frm = self.at
        before = self.lst.get(frm[1]) if isinstance(frm, tuple) else None
        _pad(f"{key} w:{STEP}")
        to = self.look()
        if isinstance(frm, tuple) and to == frm and key in ("Up", "Down") and self.lst.get(to[1]) != before:
            self.off += 1 if key == "Down" else -1   # con trỏ đứng ở mép, danh sách cuộn
            self.lst[to[1] + (1 if key == "Down" else -1)] = self.lst[to[1]]
            self.lst[to[1]] = before
            self.at = to = ("list", to[1] + (1 if key == "Down" else -1))
        if isinstance(frm, int) and record:
            self.edges[(frm, key)] = to
        return to

    def path(self, a: int, b: object) -> list[str] | None:
        """Đường bấm ngắn nhất a → b theo các cạnh đã biết."""
        prev: dict[object, tuple[object, str]] = {a: (None, "")}
        q = deque([a])
        while q:
            u = q.popleft()
            if u == b:
                keys = []
                while prev[u][0] is not None:
                    keys.append(prev[u][1])
                    u = prev[u][0]
                return keys[::-1]
            if not isinstance(u, int):
                continue
            for d in DIRS:
                v = self.edges.get((u, d))
                if v is not None and v not in prev:
                    prev[v] = (u, d)
                    q.append(v)
        return None

    def explore(self) -> None:
        """Dò đủ cạnh của mọi vị trí trên sân (bắt đầu từ chỗ đang đứng; đang ở danh sách thì bấm Trái về sân)."""
        if not isinstance(self.look(), int):
            self.press("Left")
            if not isinstance(self.at, int):
                raise PlanError("không về được sân")
        for _ in range(200):
            todo = [(n, d) for n in range(len(self.nodes)) for d in DIRS if (n, d) not in self.edges]
            if not todo:
                return
            # cạnh chưa dò gần nhất
            best = None
            for n, d in todo:
                p = [] if n == self.at else self.path(self.at, n)
                if p is not None and (best is None or len(p) < len(best[0])):
                    best = (p, n, d)
            if best is None:
                raise PlanError("đồ thị đứt")
            p, n, d = best
            for k in p:
                self.press(k)
            if self.at != n:
                raise PlanError(f"đi lạc: muốn {n}, đang {self.at}")
            to = self.press(d)
            if not isinstance(to, int):   # vào danh sách → quay lại sân
                self.press("Left")
                if self.at != n and isinstance(self.at, int):
                    self.edges[(("list",), "Left")] = self.at
        raise PlanError("dò quá lâu")

    def go(self, target: object) -> None:
        for _ in range(3):
            if self.at == target:
                return
            if isinstance(target, tuple):
                p = []
            elif isinstance(self.at, int):
                p = self.path(self.at, target)
            else:
                p = ["Left"]
            if p is None:
                raise PlanError(f"không có đường tới {target}")
            if isinstance(target, tuple) and target[0] == "list":
                if isinstance(self.at, int):   # vào danh sách qua cạnh tới danh sách bất kỳ
                    entry = next(((n, d) for (n, d), v in self.edges.items() if isinstance(v, tuple)), None)
                    if entry is None:
                        raise PlanError("chưa biết đường vào danh sách")
                    for k in self.path(self.at, entry[0]) or []:
                        self.press(k)
                    self.press(entry[1])
                row = target[1]
                for _ in range(40):
                    if not isinstance(self.at, tuple) or self.at[1] == row:
                        break
                    self.press("Down" if self.at[1] < row else "Up")
                continue
            for k in p:   # bấm liền nhiều nút thì game nuốt nút (thử 23/11) → từng nút, chụp kiểm
                self.press(k)
        if self.at != target:
            raise PlanError(f"không tới được {target} (đang {self.at})")


    def enter_list(self) -> None:
        if not isinstance(self.at, tuple):
            entry = next(((n, d) for (n, d), v in self.edges.items() if isinstance(v, tuple)), None)
            if entry is None:
                raise PlanError("chưa biết đường vào danh sách")
            self.go(entry[0])
            self.press(entry[1])
            if not isinstance(self.at, tuple):
                raise PlanError("không vào được danh sách")

    def to_top(self, rows: int = 40) -> None:
        """Kéo danh sách lên tận đầu (có thể đang cuộn sẵn), đánh số dòng lại: dòng 0 = dòng đầu thật."""
        self.enter_list()
        if self.top:
            self.go(("list", 0))
            return
        for _ in range(rows):
            prev = (self.at, self.lst.get(self.at[1]))
            self.press("Up")
            if (self.at, self.lst.get(self.at[1])) == prev:
                break
        n0 = self.lst.get(self.at[1])
        self.off -= self.at[1]
        self.lst, self.at, self.top = {0: n0}, ("list", 0), True

    def rows(self) -> dict[int, int | None]:
        """Số áo đoán từ tên 12 dòng danh sách đang hiện (dòng trên màn hình 0–11, theo ảnh vừa chụp)."""
        return {i: guess(ocr.rec(self.im, _box((455, ROW0 + ROWH * i - 10, 585, ROW0 + ROWH * i + 14)))[0], self.roster or {})
                for i in range(VISIBLE)}

    def find_list(self, shirt: int, rows: int = 40) -> tuple[str, int]:
        """Tới dòng danh sách có số áo `shirt`: dòng đã biết → đi thẳng; đọc tên các dòng đang hiện; không thì lên đầu rồi dò xuống."""
        for r, v in list(self.lst.items()):
            if v == shirt and self.top:
                self.go(("list", r))
                if self.lst.get(self.at[1]) == shirt:
                    return self.at
        self.enter_list()
        if self.roster and self.im is not None:   # đọc tên cả danh sách đang hiện → nhảy thẳng tới dòng cần
            t = next((i for i, v in self.rows().items() if v == shirt), None)
            phys = self.at[1] - self.off
            if t is not None:
                for _ in range(abs(t - phys)):   # từng nút (bấm liền game nuốt nút)
                    self.press("Down" if t > phys else "Up")
                if self.lst.get(self.at[1]) == shirt:
                    return self.at
        self.to_top(rows)
        for _ in range(rows):
            if self.lst.get(self.at[1]) == shirt:
                return self.at
            prev = self.at
            self.press("Down")
            if self.at == prev:
                break
        raise PlanError(f"không thấy số áo {shirt} trong danh sách")

    def swap(self, a: object, find) -> object:
        """Chọn ô `a` (A), tới ô thứ hai bằng find() (trả ô), A → đổi chỗ. Lỗi giữa chừng → B bỏ chọn."""
        self.go(a)
        _pad("A w:0.8")
        self.selected = True
        try:
            other = find()
            _pad("A w:1.2")
        except Exception:
            _pad("B w:0.8")
            raise
        finally:
            self.selected = False
        return other

    def tour(self) -> None:
        """Đi qua mọi vị trí trên sân (đọc số áo)."""
        left = set(range(len(self.nodes))) - {self.at}
        while left:
            nxt = min(left, key=lambda n: len(self.path(self.at, n) or [0] * 99) if isinstance(self.at, int) else 0)
            self.go(nxt)
            left -= {self.at, nxt}

    def dump(self) -> dict:
        return {"nodes": self.nodes, "edges": [[n, d, list(v) if isinstance(v, tuple) else v] for (n, d), v in self.edges.items() if isinstance(n, int)]}

    def load(self, d: dict) -> None:
        self.shirt, self.lst, self.off, self.at, self.top, self.sure = {}, {}, 0, None, False, set()
        self.nodes = [tuple(p) for p in d["nodes"]]
        self.edges = {(n, k): (tuple(v) if isinstance(v, list) else v) for n, k, v in d["edges"]}


def _key(t: str) -> str:
    return re.sub(r"[^a-z]", "", unicodedata.normalize("NFKD", t).encode("ascii", "ignore").decode().lower())


def guess(text: str, roster: dict[int, str]) -> int | None:
    """Số áo của tên đọc được (có thể bị cắt/đè: "nPauTeo", "T.Abr") — chuỗi con chung dài nhất ≥ 4 (hoặc cả tên ngắn),
    phải hơn hẳn người thứ hai."""
    t = _key(text)
    if len(t) < 3:
        return None
    sc = []
    for shirt, name in roster.items():
        n = _key(name)
        m = SequenceMatcher(None, t, n).find_longest_match(0, len(t), 0, len(n)).size
        sc.append((m, shirt, n))
    sc.sort(reverse=True)
    best = sc[0]
    ok = best[0] >= 4 or (best[0] >= 3 and best[0] == len(best[2]))
    return best[1] if ok and (len(sc) == 1 or best[0] - sc[1][0] >= 2) else None


def read_names(im: Image.Image, nodes: list[tuple[int, int]], roster: dict[int, str]) -> dict[int, int | None]:
    """Số áo đoán từ tên dưới ảnh từng vị trí trên sân (1 lần chụp). Tên sát nhau dễ đọc lẫn tên người bên cạnh → số áo nào
    đoán cho ≥ 2 vị trí thì bỏ (None = phải trỏ vào đọc)."""
    r = {i: guess(ocr.rec(im, (x - 70, y + 4, x + 8, y + 22))[0], roster) for i, (x, y) in enumerate(nodes)}
    vals = [v for v in r.values() if v is not None]
    return {i: (v if vals.count(v) == 1 else None) for i, v in r.items()}


# Toạ độ bàn tay trên sân ↔ sơ đồ (đo 23/11, sai số ≤ 1 px): x = X0 + UX·ngang, y = Y0 − UY·dọc; kéo vị trí đi đúng 1 đơn vị/lần nhấn.
UX, UY, X0, Y0 = 2.83, 6.69, 122.3, 520.0
ROLES = ["GK", "CB", "LB", "RB", "DMF", "CMF", "LMF", "RMF", "AMF", "LWF", "RWF", "SS", "CF"]
ROLE_NAMES = ["goalkeeper", "centerback", "leftback", "rightback", "defensivemidfielder", "centermidfielder", "leftmidfielder",
              "rightmidfielder", "attackingmidfielder", "leftwingforward", "rightwingforward", "secondstriker", "centerforward"]


def target(f: list[int]) -> tuple[float, float]:
    """Toạ độ bàn tay của ô sơ đồ [vai, dọc, ngang]."""
    return (X0 + UX * f[2] + _DX, Y0 - UY * f[1])


def assign(nodes: list[tuple[int, int]], formation: list[list[int]]) -> list[int]:
    """Ghép vị trí trên màn hình với ô 0–10 của sơ đồ theo toạ độ tuyệt đối (đơn vị sơ đồ), tham lam theo cặp gần nhất.
    Trận này game có thể đang ở sơ đồ khác → ô nào gần chỗ nào thì nhận chỗ đó, rồi kéo về đúng toạ độ (place). Trả slot_of[node]."""
    T = np.array([target(f) for f in formation], float)
    P = np.array(nodes, float)
    cost = (((P[:, None, :] - T[None, :, :]) / np.array([UX, UY])) ** 2).sum(-1)
    slot_of = [-1] * len(nodes)
    used = set()
    for i, j in sorted(itertools.product(range(len(nodes)), range(len(formation))), key=lambda ij: cost[ij]):
        if slot_of[i] < 0 and j not in used:
            slot_of[i] = j
            used.add(j)
    return slot_of


def _hold(key: str, n: int) -> None:
    """Dời n đơn vị theo hướng key khi đang giữ A: ít thì nhấn từng nấc, nhiều thì giữ (lặp ~15 đơn vị/s sau ~0,3 s)."""
    if n >= 5:
        _pad(f"+{key} w:{0.3 + (n - 3) / 15:.2f} -{key} w:0.35")
    else:
        _pad(" ".join([f"+{key} w:0.05 -{key} w:0.3"] * n))


def place(b: Board, formation: list[list[int]], slot_of: list[int]) -> tuple[int, int, list[str]]:
    """Kéo từng cầu thủ (trừ thủ môn) về đúng toạ độ ô của mình (±1 đơn vị) rồi chọn đúng vai (X → Select Position).
    Thả trùng chỗ người khác = đổi chỗ → né: ô đích đang có người thì để sau. Trả (số người đã dời, số vai đã đổi, vai không chọn được)."""
    moved, roles, miss = 0, 0, []
    todo = [n for n in range(len(b.nodes)) if formation[slot_of[n]][0] != 0]
    for _ in range(3):
        later = []
        for n in todo:
            tx, ty = target(formation[slot_of[n]])
            x, y = b.nodes[n]
            if abs(tx - x) <= UX * 1.6 and abs(ty - y) <= UY * 1.1:   # thả xong game làm tròn ±1 đơn vị
                continue
            if any(abs(tx - q[0]) < UX * 4 and abs(ty - q[1]) < UY * 2 for m, q in enumerate(b.nodes) if m != n):
                later.append(n)
                continue
            b.go(n)
            shirt = b.shirt.get(n)
            _pad("+A w:1.1")
            try:
                for _ in range(14):
                    h = hand(nav.shot("m17_gp"))
                    if not h or h[0] != "pitch":
                        raise PlanError("mất bàn tay khi kéo vị trí")
                    ex, ey = (tx - h[1][0]) / UX, (ty - h[1][1]) / UY
                    if abs(ex) <= 0.7 and abs(ey) <= 0.7:
                        break
                    if abs(ey) >= abs(ex) and round(abs(ey)) >= 1:
                        _hold("Up" if ey < 0 else "Down", round(abs(ey)))
                    else:
                        _hold("Left" if ex < 0 else "Right", max(1, round(abs(ex))))
            finally:
                _pad("-A w:1.2")
            h = hand(nav.shot("m17_gp"))
            if not h or h[0] != "pitch":
                raise PlanError("mất bàn tay sau khi kéo vị trí")
            b.nodes[n] = h[1]
            b.at = n
            b.look()
            if b.at != n or b.shirt.get(n) != shirt:
                raise PlanError(f"kéo vị trí làm đổi người ở vị trí {n}")
            moved += 1
        if not later or len(later) == len(todo):
            if later:
                raise PlanError(f"không dời được vị trí {later} (ô đích đang có người)")
            break
        todo = later
    over = nav.shot("m17_gp")   # nhãn vai trên sân đọc 1 lượt; chỉ trỏ vào người đọc không ra / lệch vai (trỏ từng người ~3 s/người)
    for n in range(len(b.nodes)):
        want = formation[slot_of[n]][0]
        if want == 0 or _badge(over, b.nodes[n]) == ROLES[want]:
            continue
        b.go(n)
        if _role(b.im) == ROLES[want]:
            continue
        _pad("X w:1.2")
        im = nav.shot("m17_gp")
        rows = [(y, _key(ocr.rec(im, _box((215, y - 13, 470, y + 13)))[0]).replace("centre", "center")) for y in range(240, 520, 11)]
        opts = []
        for y, t in rows:   # dòng trong bảng Select Position (cách nhau 33 px, vị trí bảng tuỳ số lựa chọn)
            sc = max(((SequenceMatcher(None, t, nm).ratio(), c) for c, nm in enumerate(ROLE_NAMES)), default=(0, None)) if len(t) >= 6 else (0, None)
            code = sc[1] if sc[0] >= 0.8 else None
            if code is not None and opts and opts[-1][1] == code and y - opts[-1][2] <= 11:
                opts[-1] = ((opts[-1][3] + y) // 2, code, y, opts[-1][3])   # giữa các dòng quét cùng chữ
            elif code is not None:
                opts.append((y, code, y, y))
        opts = [(y, c) for y, c, _, _ in opts]
        codes = [c for _, c in opts]
        cur = _role_menu_pos(im, opts)
        if want not in codes or cur is None:
            _pad("B w:1")
            miss.append(f"{ROLES[want]}@{n}")
            continue
        d = codes.index(want) - cur
        for _ in range(abs(d)):
            _pad(("Down" if d > 0 else "Up") + " w:0.5")
        _pad("A w:1.2")
        b.look()
        if _role(b.im) != ROLES[want]:
            miss.append(f"{ROLES[want]}@{n}")
        else:
            roles += 1
    return moved, roles, miss


def _badge(im: Image.Image, node: tuple[int, int]) -> str:
    """Nhãn vai in trên sân cạnh người ở `node` (toạ độ bàn tay): "CB 82" nằm bên trái-trên chỗ bàn tay ~ (x-60..x-18, y-9..y+7)."""
    x, y = node
    return re.sub(r"[^A-Z]", "", ocr.rec(im, (x - 62, y - 10, x - 16, y + 8))[0].upper())


def _role(im: Image.Image) -> str:
    """Vai của người đang trỏ (ô thông tin dưới sân: nhãn màu dưới tên)."""
    return re.sub(r"[^A-Z]", "", ocr.rec(im, _box((155, 586, 210, 607)))[0].upper())


def _role_menu_pos(im: Image.Image, opts: list[tuple[int, int]]) -> int | None:
    """Dòng đang chọn trong bảng Select Position (nền tối)."""
    if not opts:
        return None
    a = np.asarray(im.convert("L"), float)
    dark = [a[y - 8:y + 8, 230 + _DX:440 + _DX].mean() for y, _ in opts]
    return int(np.argmin(dark))


def apply(want: list[int], formation: list[list[int]], roster: dict[int, str] | None = None, cache: Path | None = None,
          bench: list[int] | None = None) -> dict:
    """Đang ở Lineup/Edit Position: đổi người tới khi ô k (0–10) là số áo want[k], kéo từng người về đúng toạ độ + vai của ô
    (formation [vai, dọc, ngang]); bench = số áo phải ngồi 12 ghế dự bị (trên vạch ngăn; thứ tự trong ghế chỉ là thứ tự hiện).
    Khung + tay cầm theo set_side. roster = {số áo: tên} của đội. Trả {"swaps", "bench", "moved", "roles", "miss", "explored", "secs"}.
    Đồ thị nhảy lưu theo khung + sơ đồ ở `cache` (dò lần đầu ~1,5 phút); đồ thị cũ không khớp màn hình → dò lại 1 lần.
    Lỗi → PlanError (người gọi cài lại + mở lại game như cũ)."""
    t0 = time.monotonic()
    key = json.dumps(formation) if _DX == 0 else f"away:{json.dumps(formation)}"
    store = {}
    if cache:
        try:
            store = json.loads(cache.read_text())
        except (OSError, ValueError):
            store = {}
    graphs = store.get(key) or []
    if isinstance(graphs, dict):   # bản cũ: 1 đồ thị/sơ đồ
        graphs = [graphs]
    good = None
    if graphs:   # nhiều bố cục cho cùng sơ đồ (bố cục game tự xếp lúc chọn đội, bố cục sau khi kéo) → chọn cái có nhãn vai đúng chỗ
        im = nav.shot("m17_gp")
        score = [sum(_badge(im, tuple(q)) in ROLES for q in g["nodes"]) for g in graphs]
        best = max(range(len(graphs)), key=lambda i: score[i])
        good = graphs[best] if score[best] >= 9 else None
    for attempt in (0, 1):
        b = Board()
        b.roster = roster
        explored = False
        try:
            if good and attempt == 0:
                b.load(good)
                b.look()
                if isinstance(b.at, tuple):
                    b.press("Left")
                if len(b.nodes) != 11:   # vị trí lạ → đồ thị cũ không khớp màn hình
                    raise PlanError(f"đồ thị đã lưu không khớp ({len(b.nodes)} vị trí)")
            else:
                b.explore()
                explored = True
                if len(b.nodes) != 11:
                    raise PlanError(f"thấy {len(b.nodes)} vị trí trên sân")
            r = _apply(b, want, formation, roster, bench)
        except PlanError:
            if attempt or explored:
                raise
            if good in graphs:
                graphs.remove(good)   # đồ thị cũ sai (game đang ở bố cục khác) → dò lại
            continue
        if cache:
            new = json.loads(json.dumps([b.pre, b.dump()] if b.pre["nodes"] != b.dump()["nodes"] else [b.dump()]))   # lúc vào + sau khi kéo
            keep = [g for g in graphs if all(g["nodes"] != n["nodes"] for n in new)]
            store[key] = (new + keep)[:4]
            cache.write_text(json.dumps(store))
        return {**r, "explored": explored or attempt == 1, "secs": round(time.monotonic() - t0, 1)}
    raise PlanError("không áp được đội hình")


def _apply(b: Board, want: list[int], formation: list[list[int]], roster: dict[int, str] | None, bench: list[int] | None) -> dict:
    slot_of = assign(b.nodes, formation)
    node_of = {s: n for n, s in enumerate(slot_of)}
    if roster:
        for n, v in read_names(b.im, b.nodes, roster).items():
            if n not in b.sure:
                b.shirt[n] = v
    for n in range(11):   # tên không đọc chắc → trỏ vào đọc số áo
        if b.shirt.get(n) is None and n not in b.sure:
            b.go(n)
    swaps, touched = 0, set()
    for s in range(11):
        n, w = node_of[s], want[s]
        if b.shirt.get(n) != w and n not in b.sure:
            b.go(n)   # đọc thật trước khi đổi
        if b.shirt.get(n) == w:
            continue
        m = next((k for k, v in b.shirt.items() if v == w and k != n), None)

        def find(m=m, w=w):
            if m is None:   # tên đoán sai/không chắc → trỏ vào các vị trí chưa đọc số áo thật trước khi tìm ở danh sách
                for k in range(11):
                    if k not in b.sure and k != n:
                        b.go(k)
                        if b.shirt.get(k) == w:
                            return b.at
                return b.find_list(w)
            b.go(m)
            if b.shirt.get(m) != w:
                raise PlanError(f"vị trí {m} không phải số áo {w}")
            return b.at
        other = b.swap(n, find)
        old = b.shirt[n]
        b.shirt[n] = w
        b.sure.discard(n)
        touched.add(n)
        if isinstance(other, int):
            b.shirt[other] = old
            b.sure.discard(other)
            touched.add(other)
        else:
            b.lst[other[1]] = old
        swaps += 1
    # kiểm: đọc thật các vị trí đã đổi (trỏ vào), các vị trí khác đọc lại tên
    for n in sorted(touched):
        b.go(n)
    got = [b.shirt.get(node_of[s]) for s in range(11)]
    if got != list(want[:11]):
        raise PlanError(f"đổi xong vẫn lệch: game {got}, cần {list(want[:11])}")
    if roster:
        names = read_names(nav.shot("m17_gp"), b.nodes, roster)
        for n in [n for n, v in names.items() if v is not None and v != b.shirt.get(n)]:   # tên lệch → trỏ vào đọc số áo thật
            b.go(n)
            if b.shirt.get(n) != want[slot_of[n]]:
                raise PlanError(f"vị trí {n}: game số áo {b.shirt.get(n)}, cần {want[slot_of[n]]}")
    benched = fix_bench(b, [x for x in (bench or [])[:VISIBLE] if x not in want[:11]]) if bench and roster else 0
    b.pre = json.loads(json.dumps(b.dump()))   # chép (place sửa b.nodes tại chỗ); đổi người không dời vị trí → bố cục lúc vào màn
    moved, roles, miss = place(b, formation, slot_of)   # sau cùng: kéo vị trí làm đồ thị nhảy cũ lệch dần
    return {"swaps": swaps, "bench": benched, "moved": moved, "roles": roles, "miss": miss}




def fix_bench(b: Board, wanted: list[int]) -> int:
    """12 dòng đầu danh sách (ghế dự bị) phải đúng tập `wanted`: người dự bị thừa ↔ người cần lên ghế đang ở dưới vạch.
    Đọc tên 12 dòng 1 lần chụp; dòng đọc không chắc thì trỏ vào đọc số áo. Trả số lượt đổi."""
    b.to_top()
    b.look()
    seen = {i: b.lst.get(i) or v for i, v in b.rows().items()}   # đã lên đầu: dòng màn hình = dòng thật
    for i in range(VISIBLE):
        if seen.get(i) is None:
            b.go(("list", i))
            seen[i] = b.lst.get(i)
            if seen[i] is None:
                raise PlanError(f"không đọc được số áo ghế dự bị {i}")
        else:
            b.lst.setdefault(i, seen[i])
    want = set(wanted)
    extra = [i for i in range(VISIBLE) if seen[i] not in want]
    missing = [w for w in wanted if w not in seen.values()]
    moved = 0
    for i, w in zip(extra, missing):
        other = b.swap(("list", i), lambda w=w: b.find_list(w))
        b.lst[other[1]], b.lst[i] = b.lst.get(i), w
        moved += 1
    if moved:   # kiểm: lên đầu, đọc lại tên 12 ghế
        b.go(("list", 0))
        bad = [v for v in b.rows().values() if v is not None and v not in want]
        if bad:
            raise PlanError(f"ghế dự bị còn người thừa {bad}")
    return moved


# ---- Chiến thuật ("_plan" của gói) qua GAME PLAN → Preset Tactics → Attacking/Defensive Instructions (thử 23/11) ----
# Mỗi màn 5 dòng; dòng đang trỏ nền tối, ô mô tả trên phải ghi "Tên : Giá trị" (lựa chọn) hoặc "Tên" (thanh 10 nấc xanh).
# Lựa chọn 2 phương án: Phải/Trái đổi qua lại. Thanh: Phải/Trái ±1 nấc. None = dòng web không chỉnh (Build Up, Attacking Zone, Pressure).
PLAN_SCREENS = (
    ("Attacking", 1, [("attackStyle", "attackingstyle", ("counter", "possession")), None, None,
                      ("positioning", "positioning", ("maintain", "flexible")), ("supportRange", "support", None)]),
    ("Defensive", 2, [("defenceStyle", "defensivestyle", ("frontline", "allout")), ("containment", "containment", ("middle", "wide")),
                      None, ("defensiveLine", "defensiveline", None), ("compactness", "compactness", None)]),
)
PLAN_DESC = (388, 172, 620, 196)      # ô mô tả: "Attacking Styles : Counter Attack"
PLAN_ROWS = (561, 588, 615, 642)      # tâm 4 dòng hiện được của danh sách dưới
MENU_TILES = (138, 245, 353, 460, 568)   # tâm x 5 ô Main Menu (Preset Tactics … Data Management), y 560–618


def _plain(t: str) -> str:
    return re.sub(r"[^a-z]", "", t.lower())


def _desc(im: Image.Image) -> tuple[str, str]:
    t = ocr.rec(im, _box(PLAN_DESC))[0]
    a, _, b = t.partition(":")
    return _plain(a), _plain(b)


def _slider(im: Image.Image) -> int | None:
    """Số nấc sáng của thanh ở dòng đang trỏ (dòng nền tối)."""
    g = np.asarray(im.convert("RGB")).astype(int)
    for y in PLAN_ROWS:
        if g[y - 3:y + 4, 74 + _DX:88 + _DX].sum(axis=2).mean() < 250:   # mép trái dòng: nền tối ≈ 70, sáng ≈ 410
            row = g[y - 1:y + 2, 385 + _DX:598 + _DX].max(axis=0)
            on = np.r_[False, (row[:, 2] > 150) & (row[:, 2] > row[:, 0] + 50), False].astype(int)
            e = np.flatnonzero(np.diff(on))
            runs = int(np.sum(e[1::2] - e[::2] >= 6))    # nấc ≈ 12 px; bỏ đầu mút thanh
            return runs if 1 <= runs <= 10 else None
    return None


def set_plan(plan: dict[str, int]) -> dict:
    """Đang ở Main Menu GAME PLAN của khung (_DX/_SLOT) → chỉnh các mục `plan` → về lại Main Menu. Trả {changed, miss}."""
    changed, miss = 0, []
    for _ in range(3):   # con trỏ Main Menu → Preset Tactics (ô đầu trong 5 ô biểu tượng; ô đang trỏ nền tối)
        g = np.asarray(nav.shot("gp_menu").convert("L")).astype(int)
        i = int(np.argmin([g[575:605, x + _DX - 30:x + _DX + 30].mean() for x in MENU_TILES]))
        if i == 0:
            break
        _pad("Left w:0.6 " * i)
    else:
        raise PlanError("không trỏ được Preset Tactics")
    _pad("A w:2")
    for title, idx, rows in PLAN_SCREENS:
        todo = [r for r in rows if r and r[0] in plan]
        if not todo:
            continue
        _pad("Down w:0.6 " * idx + "A w:2")
        if title not in ocr.rec(nav.shot("gp_ins"), _box((80, 112, 400, 140)))[0]:
            _pad("B w:2")
            raise PlanError(f"không mở được {title} Instructions")
        cur = 0
        for i, r in enumerate(rows):
            if not r or r[0] not in plan:
                continue
            key, label, opts = r
            for _ in range(i - cur):
                _pad("Down w:0.5")
            cur = i
            want = plan[key]
            ok = False
            for _ in range(4):
                im = nav.shot("gp_row")
                name, val = _desc(im)
                if label not in name:
                    break
                if opts:
                    have = next((j for j, o in enumerate(opts) if o in val), None)
                    if have == want:
                        ok = True
                        break
                    _pad("Right w:0.6")
                else:
                    have = _slider(im)
                    if have == want:
                        ok = True
                        break
                    if have is None:
                        break
                    d = want - have
                    for _ in range(abs(d)):
                        _pad(("Right" if d > 0 else "Left") + " w:0.3")
                    time.sleep(0.3)
                changed += 1
            if not ok:
                miss.append(key)
        _pad("B w:2")   # về danh sách Preset Tactics (con trỏ giữ ở mục vừa vào)
        _pad("Up w:0.6 " * idx)
    _pad("B w:2")       # về Main Menu
    return {"changed": changed, "miss": miss}


# ---- Đội trưởng + người đá phạt ("_jobs") qua GAME PLAN → Player Settings (thử 23/11) ----
# Danh sách: Appoint as Captain, Long FK Taker, Short FK Taker, FK Taker 2, Left CK Taker, Right CK Taker, PK Taker, …
# A vào mục → bàn tay trên sân (sân = sân màn Lineup dời phải 108 px), con trỏ bắt đầu ở người đang giữ việc; ô dưới trái ghi
# "số áo Tên" người đang trỏ. A chọn → về danh sách. Chỉ người đá chính được chọn (người dự bị → bỏ qua, game tự chọn).
JOB_ITEMS = {"captain": (0, "Captain"), "longFk": (1, "Long"), "shortFk": (2, "Short"), "leftCk": (4, "Left"),
             "rightCk": (5, "Right"), "penalty": (6, "PK")}
JOB_PANEL = (150, 560, 330, 592)
JOB_DX = 108


def _job_pick(shirt: int, at: tuple[float, float], roster: dict[int, str] | None) -> bool:
    """Đang trong 1 mục Player Settings: dắt bàn tay tới người số `shirt` (bàn tay dự kiến ở `at`) rồi A."""
    seen: dict[tuple[int, int], int] = {}
    for _ in range(18):
        im = nav.shot("gp_job")
        if number(im, JOB_PANEL, roster) == shirt:
            _pad("A w:1.5")
            return True
        h = hand(im, pitch_only=True)
        if not h:
            return False
        x, y = h[1]
        dx, dy = at[0] - x, at[1] - y   # theo px: sân dọc ngắn hơn đơn vị ngang → so px đỡ lắc trái/phải
        keys = [k for k, _ in sorted([("Right" if dx > 0 else "Left", abs(dx)), ("Down" if dy > 0 else "Up", abs(dy))],
                                     key=lambda k: -k[1])]
        n = seen.get((x, y), 0)     # đã đứng ở đây → lần này thử hướng khác (trục còn lại, rồi ngược lại)
        seen[(x, y)] = n + 1
        _pad((keys + [k for k in DIRS if k not in keys])[n % 4] + " w:0.5")
    return False


def set_jobs(jobs: dict[str, int], want: list[int], formation: list[list[int]], roster: dict[int, str] | None = None) -> dict:
    """Đang ở Main Menu GAME PLAN (con trỏ bất kỳ): giao việc {job: số áo} cho người đá chính (want[i] ở ô formation[i]) → về Main Menu."""
    changed, miss = 0, []
    for _ in range(3):
        g = np.asarray(nav.shot("gp_menu").convert("L")).astype(int)
        i = int(np.argmin([g[575:605, x + _DX - 30:x + _DX + 30].mean() for x in MENU_TILES]))
        if i == 2:
            break
        _pad(("Right w:0.6 " * (2 - i)) if i < 2 else ("Left w:0.6 " * (i - 2)))
    else:
        raise PlanError("không trỏ được Player Settings")
    _pad("A w:2")
    if "Player Settings" not in ocr.rec(nav.shot("gp_ps"), _box((80, 112, 400, 140)))[0]:
        raise PlanError("không mở được Player Settings")
    cur = 0
    for job, (idx, word) in sorted(JOB_ITEMS.items(), key=lambda kv: kv[1][0]):
        if job not in jobs:
            continue
        shirt = jobs[job]
        if shirt not in want:
            miss.append(job)
            continue
        _pad("Down w:0.5 " * (idx - cur) if idx > cur else "Up w:0.5 " * (cur - idx) or "w:0.1")
        cur = idx
        ok = False
        for attempt in range(2):
            _pad("A w:1.5")
            if word not in ocr.rec(nav.shot("gp_ps"), _box((80, 112, 400, 140)))[0]:
                break
            if number(nav.shot("gp_job"), JOB_PANEL, roster) == shirt:   # con trỏ bắt đầu ở người đang giữ việc
                ok = True
                _pad("B w:1.5")
                break
            if attempt:
                _pad("B w:1.5")
                break
            tx, ty = target(formation[want.index(shirt)])
            if not _job_pick(shirt, (tx + JOB_DX, ty), roster):
                _pad("B w:1.5")
                break
            changed += 1
        if not ok:
            miss.append(job)
    _pad("B w:2")   # về Main Menu
    return {"changed": changed, "miss": miss}


def set_side(side: str, slot: int = 0) -> None:
    """Khung đội (home = trái, away = phải) + tay cầm (slot) dùng cho các lần apply sau."""
    global _DX, _SLOT
    _DX, _SLOT = (AWAY_DX if side == "away" else 0), slot


def main(argv: list[str]) -> int:
    if argv[:1] == ["probe"]:
        with nav.session():
            b = Board()
            t = time.monotonic()
            b.explore()
            print(json.dumps({"secs": round(time.monotonic() - t, 1), "nodes": b.nodes, "shirt": b.shirt,
                              "edges": {f"{n}{d[0]}": v for (n, d), v in b.edges.items() if isinstance(n, int)}}))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
