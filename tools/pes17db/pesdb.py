#!/usr/bin/env python3
"""Đọc pesdb PES 2017 (Team / Player / PlayerAssignment) từ gói CPK → JSON.

  pesdb.py extract <gói.cpk> <thư-mục-ra>    — lấy *.bin và giải WESYS
  pesdb.py export <thư-mục> <ra.json>        — đội, cầu thủ, đội hình
  pesdb.py squad <thư-mục> <mã-đội>          — in đội hình một CLB
  pesdb.py move <thư-mục> <mã-cầu-thủ> <mã-đội> <ra.bin>
                                             — chuyển cầu thủ sang đội (thay dòng ở CLB cũ), ghi PlayerAssignment.bin
                                               (WESYS) để thả vào livecpk/UNI/common/etc/pesdb/

Bố cục dò tay 17/11/2026 trên HANO V6 (gói 16-eFootballHANOV6.cpk), không có tài liệu
công khai. Chắc chắn (đã kiểm chéo đội hình Arsenal):
  WESYS: ff 10 01 57 'WESYS' | u32 nén | u32 gốc | zlib
  Player  192 byte: +8 u32 mã; +0x34 tên Nhật (46); +0x62 tên áo (46); +0x90 tên (48)
                    +0x00..0x33 chỉ số đóng gói bit (little-endian 416 bit) — xem STATS;
                    đối chiếu màn Player Info trong game (Arsenal/Brentford), 17/11/2026
  Team   1400 byte: +8 u32 mã; +0x1C tên Nhật; +0xEA tên tiếng Anh (70); +0x17 = 7 đội tuyển; còn lại chưa giải
  PlayerAssignment 16 byte: u32 mã dòng, u32 cầu thủ, u32 đội, u8 số áo, u8 (5 bit thứ tự | 3 bit vai trò), u16 cờ
                    vai trò 2/4/6 (6 = đội trưởng, vd. Henderson Brentford 197 = ô 5 + 6<<5); cờ ≈ người đá phạt (chưa giải)
  leagues <thư-mục> <ra.json>                — CLB 5 giải lớn theo CompetitionEntry ({"ENG": {"name":…, "teams":[mã đội]}, …})
  world <thư-mục> <ra.json>                  — thế giới Career (PO 19/11): mọi giải VĐQG trong dữ liệu (17 giải, 2 hạng Anh/TBN/Ý/Pháp),
                                               cúp quốc gia, nước của từng CLB (CLB dự giải = nước của giải; CLB châu Âu ngoài giải = nước
                                               thật → dự C1/C2 theo sức mạnh; còn lại null = không dự giải nào)
  apply <thư-mục> <squads.json> <ra.bin>
                                             — dựng lại dòng CLB theo đội hình DB ({"mã đội": [[mã cầu thủ, số áo], ...]} theo thứ tự
                                               ô; Pes17League.Cli export-pes17-squads). Đội tuyển + CLB không có trong json giữ nguyên.
                                               Khoá "_formations": {"mã đội": [[vai trò, X, Y] × 11]} (ô 0 = GK) → ghi thêm
                                               TacticsFormation.bin cạnh <ra.bin>; cầu thủ thứ k đứng ô k (PO 21/11).
                                               Khoá "_jobs": {"mã đội": {"captain"|"penalty"|"longFk"|"shortFk"|"leftCk"|"rightCk": mã cầu thủ}}
                                               → đội trưởng + người đá phạt (JOB_BITS).
                                               Khoá "_plan": {"mã đội": {"attackStyle"|"positioning"|"defenceStyle"|"containment": 0/1,
                                               "supportRange"|"defensiveLine"|"compactness": 1..10}} → Tactics.bin cạnh <ra.bin>.
  formations <thư-mục> <ra.json>             — sơ đồ game của từng đội ({"mã đội": [[vai trò, X, Y] × 11]}) → web làm sơ đồ "Theo game"
"""
from __future__ import annotations

import json, struct, sys, zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cpk import Cpk

FILES = ("Player", "Team", "PlayerAssignment", "Competition", "CompetitionEntry", "Coach", "Tactics", "TacticsFormation")
PLAYER_SIZE, TEAM_SIZE, ASSIGN_SIZE = 192, 1400, 16


# CompetitionEntry 12 byte: u32 mã đội, …, u16 @10 mã giải (dò 18/11/2026, khớp danh sách mùa 2025-26)
LEAGUES = {9: ("ENG", "Premier League"), 0xB: ("ESP", "La Liga"), 0xA: ("ITA", "Serie A"),
           0x27: ("GER", "Bundesliga"), 0xC: ("FRA", "Ligue 1")}


def competition_entries(raw: bytes) -> list[tuple[int, int]]:
    """(mã giải, mã đội)."""
    return [(struct.unpack_from("<H", raw, i + 10)[0], struct.unpack_from("<I", raw, i)[0]) for i in range(0, len(raw) - 11, 12)]


# Thế giới Career (dò 19/11/2026 trên HANO V6): mã giải CompetitionEntry → (nước, hạng, tên, lượt, mã menu giải trong game)
WORLD_LEAGUES = {
    9: ("ENG", 1, "Premier League", 2, "eng1"), 66: ("ENG", 2, "Championship", 2, "eng2"),
    11: ("ESP", 1, "La Liga", 2, "esp1"), 67: ("ESP", 2, "LaLiga 2", 2, "esp2"),
    10: ("ITA", 1, "Serie A", 2, "ita1"), 69: ("ITA", 2, "Serie B", 2, "ita2"),
    12: ("FRA", 1, "Ligue 1", 2, "fra1"), 68: ("FRA", 2, "Ligue 2", 2, "fra2"),
    39: ("GER", 1, "Bundesliga", 2, "peu"), 13: ("NED", 1, "Eredivisie", 2, "ned1"), 14: ("POR", 1, "Liga Portugal", 2, "por1"),
    21: ("BRA", 1, "Brasileirão", 2, "bra"), 22: ("ARG", 1, "Liga Profesional", 1, "arg"), 23: ("CHI", 1, "Primera División Chile", 2, "chi"),
    40: ("USA", 1, "MLS", 2, None), 41: ("EGY", 1, "Egyptian Premier League", 2, None), 8: ("MAR", 1, "Botola Pro", 2, None),
}
# Cúp quốc gia có trong dữ liệu game (Morocco không có)
WORLD_CUPS = {
    "ENG": ("FA Cup", "FA Cup"), "ESP": ("Copa del Rey", "CdR"), "ITA": ("Coppa Italia", "CI"), "FRA": ("Coupe de France", "CdF"),
    "GER": ("DFB-Pokal", "DFB"), "NED": ("KNVB Beker", "KNVB"), "POR": ("Taça de Portugal", "Taça"), "BRA": ("Copa do Brasil", "CdB"),
    "ARG": ("Copa Argentina", "CA"), "CHI": ("Copa Chile", "CC"), "USA": ("U.S. Open Cup", "USOC"), "EGY": ("Egypt Cup", "EC"),
    "MAR": ("Coupe du Trône", "CdT"),
}
# Nước CLB (Team +20 u16 & 0x1FF) của CLB châu Âu không thuộc giải nào → dự C1/C2 dạng CLB lẻ
UEFA_LOOSE = {190: "TUR", 197: "BEL", 232: "SCO", 203: "DEN", 238: "SUI", 211: "GRE", 202: "CZE", 239: "UKR", 200: "CRO", 194: "AUT",
              208: "FRA"}
NOT_CLUB = ("classics", "ml default", "old boys")   # đội đặc biệt (huyền thoại, Master League) → không dự giải


def team_country(r: bytes) -> int:
    return struct.unpack_from("<H", r, 20)[0] & 0x1FF


def world(d: Path) -> dict:
    raw = (d / "Team.raw").read_bytes()
    recs = {struct.unpack_from("<I", raw, i + 8)[0]: raw[i:i + TEAM_SIZE] for i in range(0, len(raw) - TEAM_SIZE + 1, TEAM_SIZE)}
    info = {t["id"]: t for t in teams(raw)}
    leagues = {cid: {"comp": cid, "country": c, "tier": t, "name": n, "legs": legs, "menu": m, "teams": []}
               for cid, (c, t, n, legs, m) in WORLD_LEAGUES.items()}
    for comp, team in competition_entries((d / "CompetitionEntry.raw").read_bytes()):
        if comp in leagues and team not in leagues[comp]["teams"]:
            leagues[comp]["teams"].append(team)
    country = {}
    for l in leagues.values():
        for t in l["teams"]:
            if t in country:
                raise SystemExit(f"đội {t} ở 2 giải")
            country[t] = l["country"]
    for tid, t in info.items():
        if t["national"] or tid in country:
            continue
        name = t["name"].lower()
        country[tid] = None if any(x in name for x in NOT_CLUB) else UEFA_LOOSE.get(team_country(recs[tid]))
    return {
        "leagues": sorted(leagues.values(), key=lambda l: (l["country"], l["tier"])),
        "cups": [{"country": c, "name": n, "short": s} for c, (n, s) in WORLD_CUPS.items()],
        "clubs": {str(k): v for k, v in sorted(country.items())},
    }


POSITIONS = ("GK", "CB", "LB", "RB", "DMF", "CMF", "LMF", "RMF", "AMF", "LWF", "RWF", "SS", "CF")
# tên: (bit, độ rộng, cộng thêm). Đã khớp trên 5–25 cầu thủ mẫu; một kết quả duy nhất.
STATS = {
    "height": (114, 7, 132), "weight": (121, 7, 30), "age": (280, 5, 15),
    "position": (297, 4, 0), "form": (301, 3, 1), "leftFoot": (374, 1, 0),
    "setPieceTaking": (135, 6, 40), "lowPass": (141, 6, 40), "clearing": (147, 6, 40),
    "defensiveProwess": (153, 6, 40), "ballControl": (160, 6, 40), "header": (166, 6, 40),
    "jump": (172, 6, 40), "catching": (178, 6, 40), "coverage": (184, 6, 40),
    "speed": (192, 6, 40), "ballWinning": (198, 6, 40), "finishing": (204, 6, 40),
    "reflexes": (210, 6, 40), "goalkeeping": (216, 6, 40), "curve": (224, 6, 40),
    "stamina": (230, 6, 40), "explosivePower": (236, 6, 40), "dribbling": (242, 6, 40),
    "kickingPower": (248, 6, 40), "attackingProwess": (256, 6, 40), "bodyControl": (262, 6, 40),
    "physicalContact": (268, 6, 40), "loftedPass": (274, 6, 40),
}
# Chưa chắc (dữ liệu mẫu ít biến thiên): chân không thuận, chống chấn thương, kỹ năng, vị trí phụ.


def stats(r: bytes) -> dict:
    v = int.from_bytes(r[:0x34], "little")
    out = {k: ((v >> b) & ((1 << w) - 1)) + add for k, (b, w, add) in STATS.items()}
    out["position"] = POSITIONS[out["position"]] if out["position"] < len(POSITIONS) else str(out["position"])
    out["leftFoot"] = bool(out["leftFoot"])
    return out


def wesys(b: bytes) -> bytes:
    if b[3:8] != b"WESYS":
        return b
    n, u = struct.unpack("<II", b[8:16])
    d = zlib.decompress(b[16:16 + n])
    if len(d) != u:
        raise ValueError("WESYS: sai cỡ sau giải nén")
    return d


def pack_wesys(raw: bytes, head: bytes) -> bytes:
    """Gói lại theo header của file gốc (4 byte đầu + 'WESYS'); nén zlib mức 9."""
    z = zlib.compress(raw, 9)
    return head[:8] + struct.pack("<II", len(z), len(raw)) + z


def move(raw: bytes, player: int, team: int, national: set[int]) -> tuple[bytes, dict]:
    """Đổi đội của dòng CLB (không phải đội tuyển) của cầu thủ; số áo = số trống nhỏ nhất ở đội mới, thứ tự = ô trống nhỏ nhất."""
    b = bytearray(raw)
    rows = assignments(raw)
    mine = [i for i, a in enumerate(rows) if a["player"] == player and a["team"] not in national]
    if len(mine) != 1:
        raise SystemExit(f"cầu thủ {player}: {len(mine)} dòng CLB (cần đúng 1)")
    i = mine[0]
    dest = [a for a in rows if a["team"] == team]
    if len(dest) >= 32:
        raise SystemExit(f"đội {team} đã đủ 32")
    used = {a["shirt"] for a in dest}
    shirt = rows[i]["shirt"] if rows[i]["shirt"] not in used else next(n for n in range(1, 100) if n not in used)
    taken = {a["order"] for a in dest}
    order = next(n for n in range(32) if n not in taken)
    old = rows[i]["team"]
    struct.pack_into("<IBBH", b, i * ASSIGN_SIZE + 8, team, shirt, order, 0)  # sang đội mới: bỏ vai trò/cờ
    # đội cũ để trống ô thứ tự (game vẫn có khoảng trống sẵn, vd. Brentford thiếu 5) — không dồn để khỏi xáo đội hình chính
    return bytes(b), {"from": old, "to": team, "shirt": shirt, "order": order}


def text(b: bytes) -> str:
    return b.split(b"\0", 1)[0].decode("utf-8", "replace")


def players(raw: bytes) -> list[dict]:
    out = []
    for i in range(0, len(raw) - PLAYER_SIZE + 1, PLAYER_SIZE):
        r = raw[i:i + PLAYER_SIZE]
        out.append({
            "id": struct.unpack_from("<I", r, 8)[0],
            "name": text(r[0x90:0xC0]),
            "shirtName": text(r[0x62:0x90]),
            "nameJa": text(r[0x34:0x62]),
            **stats(r),
            "raw": r[:0x34].hex(),
        })
    return out


def teams(raw: bytes) -> list[dict]:
    out = []
    for i in range(0, len(raw) - TEAM_SIZE + 1, TEAM_SIZE):
        r = raw[i:i + TEAM_SIZE]
        out.append({
            "id": struct.unpack_from("<I", r, 8)[0],
            "name": text(r[0xEA:0xEA + 70]) or text(r[0x1C:0xEA]),
            "nameJa": text(r[0x1C:0xEA]),
            "national": r[0x17] == 7,  # đội tuyển quốc gia (127 đội); CLB = 6 hoặc 2
        })
    return out


def assignments(raw: bytes) -> list[dict]:
    out = []
    for i in range(0, len(raw) - ASSIGN_SIZE + 1, ASSIGN_SIZE):
        e, p, t, shirt, order, flags = struct.unpack_from("<IIIBBH", raw, i)
        out.append({"entry": e, "player": p, "team": t, "shirt": shirt, "order": order & 31, "role": order >> 5, "flags": flags})
    return out


def apply(raw: bytes, squads: dict[int, list[tuple[int, int]]], national: set[int]) -> tuple[bytes, dict]:
    """Dựng lại các dòng CLB có trong `squads` (thứ tự ô = vị trí trong danh sách). Cầu thủ ở lại giữ mã dòng, vai trò, cờ;
    cầu thủ mới nhận mã dòng mới, vai trò/cờ 0. Dòng mới chèn ngay sau nhóm dòng cũ của đội. Đội tuyển không đụng."""
    rows = assignments(raw)
    if national & squads.keys():
        raise SystemExit(f"squads có đội tuyển: {sorted(national & squads.keys())[:5]}")
    seen: dict[int, int] = {}
    for t, sq in squads.items():
        if len(sq) > 32:
            raise SystemExit(f"đội {t}: {len(sq)} cầu thủ (> 32)")
        if len({n for _, n in sq}) != len(sq):
            raise SystemExit(f"đội {t}: trùng số áo")
        for p, _ in sq:
            if p in seen:
                raise SystemExit(f"cầu thủ {p} ở 2 đội {seen[p]} và {t}")
            seen[p] = t
    # CLB ngoài json vẫn giữ dòng gốc → không được trùng cầu thủ với đội trong json
    clash = [a["player"] for a in rows if a["team"] not in squads and a["team"] not in national and a["player"] in seen]
    if clash:
        raise SystemExit(f"{len(clash)} cầu thủ vừa ở CLB ngoài json vừa trong json, vd. {clash[:5]}")
    old = {(a["team"], a["player"]): a for a in rows if a["team"] in squads}
    next_entry = max(a["entry"] for a in rows) + 1
    out, done, stats = [], set(), {"kept": 0, "added": 0, "removed": 0}

    def emit(t: int):
        nonlocal next_entry
        for order, (p, shirt) in enumerate(squads[t]):
            a = old.get((t, p))
            if a:
                stats["kept"] += 1
                e, role, flags = a["entry"], a["role"], a["flags"]
            else:
                stats["added"] += 1
                e, role, flags, next_entry = next_entry, 0, 0, next_entry + 1
            out.append(struct.pack("<IIIBBH", e, p, t, shirt, order | role << 5, flags))

    for a in rows:
        t = a["team"]
        if t not in squads:
            out.append(struct.pack("<IIIBBH", a["entry"], a["player"], t, a["shirt"], a["order"] | a["role"] << 5, a["flags"]))
        elif t not in done:
            done.add(t)
            emit(t)
    for t in squads.keys() - done:  # đội trước đó không có dòng nào
        emit(t)
    stats["removed"] = sum(1 for (t, p) in old if seen.get(p) != t)
    return b"".join(out), stats



# Đội trưởng + người đá phạt (dò 21/11/2026 trên HANO V6 bằng màn Game Plan > Player Settings): mỗi đội đúng 1 người mang mỗi bit.
#   cờ (u16 cuối dòng PlayerAssignment): 1 phạt góc trái, 2 đá phạt xa, 4 penalty, 8 đội trưởng
#   vai trò (3 bit cao byte order): 2 phạt góc phải, 4 đá phạt gần (bit 1 hiếm, chưa rõ → giữ). FK Taker 2 game tự chọn.
#   Người được giao phải đá chính; không thì game tự thay người khác.
JOBS = ("captain", "penalty", "longFk", "shortFk", "leftCk", "rightCk")
JOB_BITS = {"captain": ("flags", 8), "penalty": ("flags", 4), "longFk": ("flags", 2), "shortFk": ("role", 4),
            "leftCk": ("flags", 1), "rightCk": ("role", 2)}


def apply_jobs(raw: bytes, jobs: dict[int, dict[str, int]]) -> tuple[bytes, list[int]]:
    """jobs: đội → {vai: mã cầu thủ}. Xoá 6 bit vai của cả đội rồi gán lại; vai không có / người không thuộc đội → bỏ trống.
    Trả (PlayerAssignment mới, đội có người không thuộc đội)."""
    b, bad = bytearray(raw), []
    flag_mask = sum(v for f, v in JOB_BITS.values() if f == "flags")
    role_mask = sum(v for f, v in JOB_BITS.values() if f == "role")
    for i in range(0, len(b) - ASSIGN_SIZE + 1, ASSIGN_SIZE):
        _, p, t, _, order, flags = struct.unpack_from("<IIIBBH", b, i)
        if t not in jobs:
            continue
        role, flags = (order >> 5) & ~role_mask, flags & ~flag_mask
        for job, who in jobs[t].items():
            if who == p:
                f, v = JOB_BITS[job]
                if f == "flags":
                    flags |= v
                else:
                    role |= v
        struct.pack_into("<BH", b, i + 13, (order & 31) | role << 5, flags)
    members = {(a["team"], a["player"]) for a in assignments(bytes(b))}
    for t, js in jobs.items():
        if any(job not in JOB_BITS or (t, p) not in members for job, p in js.items()):
            bad.append(t)
    return bytes(b), bad


# ---------- sơ đồ đội (dò 21/11/2026 trên HANO V6, kiểm bằng màn Game Plan) ----------
# Tactics 12 byte: u32 đội, u16 mã chiến thuật, 3 byte thông số (PLAN_* bên dưới) + 3 byte 0. Đội có 2+ dòng; dòng ĐẦU có trong TacticsFormation
#   thường là chiến thuật game dùng (Aston Villa 107 → 748 = 4-2-1-3 như màn Game Plan) nhưng không luôn (thí nghiệm 21/11) → ghi mọi dòng. Mỗi mã chỉ thuộc 1 đội.
# TacticsFormation 12 byte: u32 vai trò (POSITIONS: 0 GK … 12 CF), u16 mã chiến thuật, u8 X (sâu: GK 3 … CF 43), u8 Y (ngang: trái 15 … phải 89),
#   u32 ô = phương án*16 + ô (3 phương án × 11 ô).
# Game xếp cầu thủ thứ k (PlayerAssignment.order) vào ô k (kiểm 21/11 trên Game Plan; danh sách màn Kickoff thì sắp theo vai trò).
TF_SIZE = 12


def primary_tactics(tactics_raw: bytes, tf_raw: bytes) -> dict[int, int]:
    """đội → mã chiến thuật game dùng."""
    ids = {struct.unpack_from("<H", tf_raw, i + 4)[0] for i in range(0, len(tf_raw), TF_SIZE)}
    out: dict[int, int] = {}
    for i in range(0, len(tactics_raw) - 11, 12):
        team, tid = struct.unpack_from("<IH", tactics_raw, i)
        if tid in ids and team not in out:
            out[team] = tid
    return out


def formations(tactics_raw: bytes, tf_raw: bytes) -> dict[int, list[list[int]]]:
    """đội → 11 ô [vai trò, X, Y] (phương án 0, theo số ô)."""
    prim = primary_tactics(tactics_raw, tf_raw)
    want = {tid: team for team, tid in prim.items()}
    out: dict[int, list] = {}
    for i in range(0, len(tf_raw), TF_SIZE):
        role, tid, x, y, slot = struct.unpack_from("<IHBBI", tf_raw, i)
        if tid in want and slot < 11:
            out.setdefault(want[tid], [None] * 11)[slot] = [role, x, y]
    return {t: f for t, f in out.items() if None not in f}


def team_tactics(tactics_raw: bytes, tf_raw: bytes) -> dict[int, list[int]]:
    """đội → mọi mã chiến thuật có sơ đồ (dòng có trong TacticsFormation)."""
    ids = {struct.unpack_from("<H", tf_raw, i + 4)[0] for i in range(0, len(tf_raw), TF_SIZE)}
    out: dict[int, list[int]] = {}
    for i in range(0, len(tactics_raw) - 11, 12):
        team, tid = struct.unpack_from("<IH", tactics_raw, i)
        if tid in ids:
            out.setdefault(team, []).append(tid)
    return out


def apply_formations(tactics_raw: bytes, tf_raw: bytes, forms: dict[int, list[list[int]]]) -> tuple[bytes, list[int]]:
    """Ghi sơ đồ (cả 3 phương án) vào MỌI chiến thuật có sơ đồ của từng đội (thí nghiệm 21/11: game không luôn dùng dòng đầu).
    Trả (TacticsFormation mới, đội bỏ qua vì không có chiến thuật)."""
    tids = team_tactics(tactics_raw, tf_raw)
    for t, f in forms.items():
        if len(f) != 11 or f[0][0] != 0 or any(r[0] == 0 for r in f[1:]) or any(not 0 < r[0] < len(POSITIONS) for r in f[1:]):
            raise SystemExit(f"đội {t}: sơ đồ sai (cần 11 ô, ô 0 là GK, ô khác 1..12)")
        if any(not (1 <= x <= 60 and 1 <= y <= 103) for _, x, y in f):
            raise SystemExit(f"đội {t}: toạ độ ngoài sân")
    by_tid = {tid: f for t, f in forms.items() for tid in tids.get(t, [])}
    b = bytearray(tf_raw)
    hit: dict[int, int] = {}
    for i in range(0, len(b), TF_SIZE):
        tid = struct.unpack_from("<H", b, i + 4)[0]
        f = by_tid.get(tid)
        if f is None:
            continue
        slot = struct.unpack_from("<I", b, i + 8)[0] & 15
        if slot < 11:
            role, x, y = f[slot]
            struct.pack_into("<I", b, i, role)
            b[i + 6], b[i + 7] = x, y
            hit[tid] = hit.get(tid, 0) + 1
    bad = [t for t in forms if t not in tids or any(hit.get(tid) != 33 for tid in tids[t])]
    return bytes(b), bad


# Thông số chiến thuật (dò 21/11/2026 bằng thí nghiệm trên Aston Villa, màn Game Plan > Preset Tactics / Instructions):
#   byte 6..8 của dòng Tactics = 24 bit: bit 0-3 Compactness, 4-7 Support Range, 8-11 Defensive Line (giá trị = mức 1..10 trừ 1);
#   trường 2 bit (chỉ giá trị 1 = phương án 2; 0/2/3 đều hiện phương án 1): 16 Positioning (Maintain Formation/Flexible),
#   18 Attacking Style (Counter Attack/Possession Game), 20 Containment Area (Middle/Wide), 22 Defensive Style (Frontline Pressure/All-out Defense).
#   Bit 12-15, các bit lẻ 17..23: chưa thấy hiện gì (giữ nguyên khi không ghi trường đó). Byte 9..11 luôn 0 — ghi 0xFFFF vào 10..11 làm game treo.
#   Build Up / Attacking Zone / Pressure: CHƯA tìm ra (không nằm trong 24 bit này) → giữ của game.
#   Game không luôn đọc cùng một dòng của đội (có lúc dòng có sơ đồ, có lúc dòng phụ) → ghi như nhau vào mọi dòng của đội.
PLAN_SLIDERS = {"compactness": 0, "supportRange": 4, "defensiveLine": 8}
PLAN_OPTIONS = {"positioning": 16, "attackStyle": 18, "containment": 20, "defenceStyle": 22}


def apply_plan(tactics_raw: bytes, plans: dict[int, dict[str, int]]) -> tuple[bytes, list[int]]:
    """plans: đội → {trường: giá trị} (thanh 1..10, lựa chọn 0/1); trường không có = giữ của game. Trả (Tactics mới, đội không có dòng nào)."""
    for t, pl in plans.items():
        for k, v in pl.items():
            if not ((k in PLAN_SLIDERS and 1 <= v <= 10) or (k in PLAN_OPTIONS and v in (0, 1))):
                raise SystemExit(f"đội {t}: {k}={v} không hợp lệ")
    b, seen = bytearray(tactics_raw), set()
    for i in range(0, len(b) - 11, 12):
        team = struct.unpack_from("<I", b, i)[0]
        pl = plans.get(team)
        if not pl:
            continue
        seen.add(team)
        v = b[i + 6] | b[i + 7] << 8 | b[i + 8] << 16
        for k, val in pl.items():
            sh, width, val = (PLAN_SLIDERS[k], 15, val - 1) if k in PLAN_SLIDERS else (PLAN_OPTIONS[k], 3, val)
            v = v & ~(width << sh) | val << sh
        b[i + 6:i + 9] = bytes([v & 255, v >> 8 & 255, v >> 16])
    return bytes(b), [t for t in plans if t not in seen]


def load(d: Path) -> dict:
    return {
        "players": players((d / "Player.raw").read_bytes()),
        "teams": teams((d / "Team.raw").read_bytes()),
        "assignments": assignments((d / "PlayerAssignment.raw").read_bytes()),
    }


def main(argv: list[str]) -> int:
    if len(argv) == 3 and argv[0] == "extract":
        c, out = Cpk(argv[1]), Path(argv[2])
        out.mkdir(parents=True, exist_ok=True)
        for f in FILES:
            blob = c.read(f"common/etc/pesdb/{f}.bin")
            (out / f"{f}.bin").write_bytes(blob)
            raw = wesys(blob)
            (out / f"{f}.raw").write_bytes(raw)
            print(f, len(raw))
        return 0
    if len(argv) == 3 and argv[0] == "export":
        db = load(Path(argv[1]))
        Path(argv[2]).write_text(json.dumps(db, ensure_ascii=False))
        print({k: len(v) for k, v in db.items()})
        return 0
    if len(argv) == 3 and argv[0] == "squad":
        db = load(Path(argv[1]))
        pl = {p["id"]: p for p in db["players"]}
        tid = int(argv[2])
        print(next((t["name"] for t in db["teams"] if t["id"] == tid), "?"))
        for a in sorted((a for a in db["assignments"] if a["team"] == tid), key=lambda a: a["order"]):
            p = pl.get(a["player"], {})
            print(f"{a['order']:>3} #{a['shirt']:<3} {p.get('name', '?'):<24} {p.get('position', '?'):<4}"
                  f" {p.get('age', '?'):>2}t {p.get('height', '?')}cm {'T' if p.get('leftFoot') else 'P'}"
                  f" tốc {p.get('speed', '?')} sút {p.get('finishing', '?')} id={a['player']}")
        return 0
    if len(argv) == 5 and argv[0] == "move":
        d = Path(argv[1])
        nat = {t["id"] for t in teams((d / "Team.raw").read_bytes()) if t["national"]}
        raw, info = move((d / "PlayerAssignment.raw").read_bytes(), int(argv[2]), int(argv[3]), nat)
        head = (d / "PlayerAssignment.bin").read_bytes()[:8]
        out = pack_wesys(raw, head)
        assert wesys(out) == raw
        Path(argv[4]).write_bytes(out)
        print(info, len(out))
        return 0
    if len(argv) == 3 and argv[0] == "leagues":
        out = {code: {"name": name, "teams": []} for code, name in LEAGUES.values()}
        for comp, team in competition_entries((Path(argv[1]) / "CompetitionEntry.raw").read_bytes()):
            if comp in LEAGUES:
                out[LEAGUES[comp][0]]["teams"].append(team)
        Path(argv[2]).write_text(json.dumps(out, ensure_ascii=False))
        print({k: len(v["teams"]) for k, v in out.items()})
        return 0
    if len(argv) == 3 and argv[0] == "world":
        w = world(Path(argv[1]))
        Path(argv[2]).write_text(json.dumps(w, ensure_ascii=False, indent=1))
        loose = sum(1 for c in w["clubs"].values() if c and c not in {l["country"] for l in w["leagues"]})
        print(" ".join(f"{l['name']}={len(l['teams'])}" for l in w["leagues"]), f"| cúp {len(w['cups'])} | CLB lẻ châu Âu {loose}",
              f"| không dự {sum(1 for c in w['clubs'].values() if c is None)}")
        return 0
    if len(argv) == 4 and argv[0] == "apply":
        d = Path(argv[1])
        nat = {t["id"] for t in teams((d / "Team.raw").read_bytes()) if t["national"]}
        doc = json.loads(Path(argv[2]).read_text())
        forms = {int(k): v for k, v in (doc.pop("_formations", None) or {}).items()}
        jobs = {int(k): {j: int(p) for j, p in v.items()} for k, v in (doc.pop("_jobs", None) or {}).items()}
        plans = {int(k): {f: int(x) for f, x in v.items()} for k, v in (doc.pop("_plan", None) or {}).items()}
        squads = {int(k): [(int(p), int(n)) for p, n in v] for k, v in doc.items()}
        raw, info = apply((d / "PlayerAssignment.raw").read_bytes(), squads, nat)
        if jobs:   # đội trưởng + người đá phạt từ web (PO 21/11)
            raw, bad = apply_jobs(raw, jobs)
            info["jobs"] = len(jobs) - len(bad)
            if bad:
                info["badJobs"] = bad[:10]
        out = pack_wesys(raw, (d / "PlayerAssignment.bin").read_bytes()[:8])
        assert wesys(out) == raw
        Path(argv[3]).write_bytes(out)
        tf_out = Path(argv[3]).with_name("TacticsFormation.bin")
        tf_out.unlink(missing_ok=True)
        if forms:   # sơ đồ web → TacticsFormation.bin cạnh PlayerAssignment.bin (deploy.sh đóng gói nếu có)
            tf, bad = apply_formations((d / "Tactics.raw").read_bytes(), (d / "TacticsFormation.raw").read_bytes(), forms)
            packed = pack_wesys(tf, (d / "TacticsFormation.bin").read_bytes()[:8])
            assert wesys(packed) == tf
            tf_out.write_bytes(packed)
            info["formations"] = len(forms) - len(bad)
            if bad:
                info["noTactics"] = bad[:10]
        tac_out = Path(argv[3]).with_name("Tactics.bin")
        tac_out.unlink(missing_ok=True)
        if plans:   # thông số chiến thuật PES17 từ web (PO 21/11) → Tactics.bin cạnh PlayerAssignment.bin
            tac, bad = apply_plan((d / "Tactics.raw").read_bytes(), plans)
            packed = pack_wesys(tac, (d / "Tactics.bin").read_bytes()[:8])
            assert wesys(packed) == tac
            tac_out.write_bytes(packed)
            info["plans"] = len(plans) - len(bad)
            if bad:
                info["noPlanRows"] = bad[:10]
        print(info, len(out))
        return 0
    if len(argv) == 3 and argv[0] == "formations":
        d = Path(argv[1])
        f = formations((d / "Tactics.raw").read_bytes(), (d / "TacticsFormation.raw").read_bytes())
        Path(argv[2]).write_text(json.dumps({str(k): v for k, v in sorted(f.items())}))
        print(len(f), "đội có sơ đồ")
        return 0
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
