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


class PlanError(Exception):
    pass


def hand(im: Image.Image) -> tuple[str, object] | None:
    """Vị trí bàn tay: ("pitch", (x, y)) | ("list", dòng) | None."""
    a = cv2.cvtColor(np.asarray(im), cv2.COLOR_RGB2HSV)
    m = ((a[..., 2] > 235) & (a[..., 1] < 25)).astype(np.uint8)
    m[:, :80] = 0
    m[:, 640:] = 0
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
    if x >= LIST_X:
        return ("list", int(round((y - ROW0) / ROWH)))
    return ("pitch", (int(x), int(y)))


def number(im: Image.Image, box=PANEL) -> int | None:
    m = re.match(r"\s*(\d{1,3})\D", ocr.rec(im, box)[0] + " ")
    return int(m[1]) if m else None


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
            self.lst[self.at[1]] = number(im, PANEL2 if self.selected else PANEL)
        else:
            self.at = self._node(h[1])
            v = number(im, PANEL2 if self.selected else PANEL)   # đọc thật mỗi lần trỏ qua → đè số đoán từ tên
            if v is not None or self.at not in self.shirt:
                self.shirt[self.at] = v
                self.sure.add(self.at)
        self.im = im
        return self.at

    def press(self, key: str, record: bool = True) -> object:
        frm = self.at
        before = self.lst.get(frm[1]) if isinstance(frm, tuple) else None
        nav.pad(f"{key} w:{STEP}")
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


    def find_list(self, shirt: int, rows: int = 40) -> tuple[str, int]:
        """Tới dòng danh sách có số áo `shirt`. Lần đầu: vào danh sách, lên tận đầu (danh sách có thể đang cuộn sẵn),
        đánh số dòng lại từ 0 rồi dò xuống (cuộn khi cần)."""
        for r, v in list(self.lst.items()):
            if v == shirt and self.top:
                self.go(("list", r))
                if self.lst.get(self.at[1]) == shirt:
                    return self.at
        if not isinstance(self.at, tuple):
            entry = next(((n, d, v) for (n, d), v in self.edges.items() if isinstance(v, tuple)), None)
            if entry is None:
                raise PlanError("chưa biết đường vào danh sách")
            self.go(entry[0])
            self.press(entry[1])
        if self.roster and self.im is not None:   # đọc tên cả danh sách đang hiện → nhảy thẳng tới dòng cần
            vis = {i: guess(ocr.rec(self.im, (455, ROW0 + ROWH * i - 10, 585, ROW0 + ROWH * i + 14))[0], self.roster) for i in range(12)}
            t = next((i for i, v in vis.items() if v == shirt), None)
            phys = self.at[1] - self.off
            if t is not None:
                if t != phys:
                    nav.pad(" ".join([("Down" if t > phys else "Up") + " w:0.25"] * abs(t - phys)) + " w:0.3")
                    self.look()
                if self.lst.get(self.at[1]) == shirt:
                    return self.at
        if not self.top:
            for _ in range(rows):
                prev = (self.at, self.lst.get(self.at[1]))
                self.press("Up")
                if (self.at, self.lst.get(self.at[1])) == prev:
                    break
            n0 = self.lst.get(self.at[1])
            self.off -= self.at[1]
            self.lst, self.at, self.top = {0: n0}, ("list", 0), True
        else:
            self.go(("list", 0))
        for _ in range(rows):
            if self.lst.get(self.at[1]) == shirt:
                return self.at
            prev = self.at
            self.press("Down")
            if self.at == prev:
                break
        raise PlanError(f"không thấy số áo {shirt} trong danh sách")

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
    """Số áo đoán từ tên dưới ảnh từng vị trí trên sân (1 lần chụp)."""
    return {i: guess(ocr.rec(im, (x - 85, y + 4, x + 12, y + 22))[0], roster) for i, (x, y) in enumerate(nodes)}


def assign(nodes: list[tuple[int, int]], formation: list[list[int]]) -> list[int]:
    """Ghép vị trí trên màn hình với ô 0–10 của sơ đồ (_formations: [vai, dọc 0–50, ngang 0–100]): chuẩn hoá cả hai về [0,1]
    theo khoảng của chính nó rồi ghép chi phí nhỏ nhất (11! quá nhiều → tham lam theo cặp gần nhất, đủ vì các ô cách xa nhau).
    Trả slot_of[node]."""
    P = np.array(nodes, float)
    F = np.array([[f[2], f[1]] for f in formation], float)      # ngang → x màn hình, dọc → y màn hình (lên trên = tấn công)
    F[:, 1] = -F[:, 1]
    norm = lambda A: (A - A.min(0)) / np.maximum(A.max(0) - A.min(0), 1e-6)
    p, f = norm(P), norm(F)
    cost = ((p[:, None, :] - f[None, :, :]) ** 2).sum(-1)
    slot_of = [-1] * len(nodes)
    used = set()
    for i, j in sorted(itertools.product(range(len(nodes)), range(len(formation))), key=lambda ij: cost[ij]):
        if slot_of[i] < 0 and j not in used:
            slot_of[i] = j
            used.add(j)
    return slot_of


def apply(want: list[int], formation: list[list[int]], roster: dict[int, str] | None = None, cache: Path | None = None) -> dict:
    """Đang ở Lineup/Edit Position: đổi người tới khi ô k (0–10) là số áo want[k]. roster = {số áo: tên} của đội (đọc tên trên sân
    1 lần chụp; thiếu thì trỏ từng người). Trả {"swaps", "explored", "secs"}. Đồ thị nhảy lưu theo sơ đồ ở `cache`
    (dò lần đầu ~1,5 phút). Lỗi → PlanError (người gọi cài lại + mở lại game như cũ)."""
    t0 = time.monotonic()
    key = json.dumps(formation)
    store = {}
    if cache:
        try:
            store = json.loads(cache.read_text())
        except (OSError, ValueError):
            store = {}
    b = Board()
    b.roster = roster
    explored = False
    if key in store:
        b.load(store[key])
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
        if cache:
            store[key] = b.dump()
            cache.write_text(json.dumps(store))
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
        b.go(n)
        nav.pad("A w:0.8")
        b.selected = True
        try:
            if m is not None:
                b.go(m)
                if b.shirt.get(m) != w:
                    raise PlanError(f"vị trí {m} không phải số áo {w}")
            else:
                b.find_list(w)
            other = b.at
            nav.pad("A w:1.2")
        except Exception:
            nav.pad("B w:0.8")   # bỏ chọn
            raise
        finally:
            b.selected = False
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
        bad = [n for n, v in names.items() if v is not None and v != b.shirt.get(n)]
        if bad:
            raise PlanError(f"tên trên sân không khớp ở vị trí {bad}")
    return {"swaps": swaps, "explored": explored, "secs": round(time.monotonic() - t0, 1)}


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
