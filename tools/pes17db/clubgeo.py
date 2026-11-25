#!/usr/bin/env python3
"""Toạ độ sân nhà của CLB (PO 25/11: thời tiết trận theo ngoài đời) — tra Wikidata một lần, ghi JSON cho web.

  clubgeo.py IN.tsv OUT.json     IN: mỗi dòng "ClubId<TAB>Tên<TAB>Mã nước" (psql -At -F$'\\t')

OUT: {ClubId: {"qid", "venue", "city", "lat", "lon"}}; CLB không tìm được → không có khoá (web dùng mặc định).
Chạy lại thì giữ kết quả cũ (chỉ tra CLB chưa có) → admin sửa tay trong OUT không bị ghi đè.
Thứ tự lấy toạ độ: sân nhà (P115) → trụ sở (P159) → nơi đặt (P131)."""
from __future__ import annotations

import json, re, sys, time, urllib.parse, urllib.request
from pathlib import Path

API = "https://www.wikidata.org/w/api.php"
UA = "pes17-league-venue/1.0"
CLUB_WORDS = ("football club", "soccer club", "association football", "football team", "sports club")


def get(params: dict) -> dict:
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=30) as r:
                return json.load(r)
        except Exception:
            time.sleep(2 + attempt * 3)
    return {}


def search(name: str) -> str | None:
    """Q-id của CLB: thử tên đầy đủ rồi tên bỏ tiền tố/hậu tố FC, CF, AC…"""
    short = re.sub(r"\b(FC|CF|AC|AFC|SC|CD|CA|SV|VfL|VfB|TSG|RC|AS|SS|US|SL|SK|FK|NK|KV|KRC|RSC|R\.)\b\.?", "", name).strip(" .-")
    for q in dict.fromkeys([name, short]):
        if not q:
            continue
        for x in get({"action": "wbsearchentities", "search": q, "language": "en", "type": "item", "limit": 7}).get("search", []):
            d = (x.get("description") or "").lower()
            if any(w in d for w in CLUB_WORDS) and "women" not in d and "youth" not in d and "reserve" not in d:
                return x["id"]
        time.sleep(0.3)
    return None


def claims(ids: list[str]) -> dict:
    out = {}
    for i in range(0, len(ids), 50):
        out.update(get({"action": "wbgetentities", "ids": "|".join(ids[i:i + 50]), "props": "claims|labels", "languages": "en"}).get("entities", {}))
    return out


def first(e: dict, prop: str):
    for c in e.get("claims", {}).get(prop, []):
        v = c.get("mainsnak", {}).get("datavalue", {}).get("value")
        if v is not None:
            return v
    return None


def label(e: dict) -> str | None:
    return e.get("labels", {}).get("en", {}).get("value")


def main(argv: list[str]) -> int:
    src, dst = Path(argv[1]), Path(argv[2])
    done = json.loads(dst.read_text()) if dst.exists() else {}
    rows = [l.split("\t") for l in src.read_text(encoding="utf-8").splitlines() if l.strip()]
    todo = [r for r in rows if r[0] not in done]
    cache = dst.with_suffix(".qid.json")   # Q-id đã tra (cả CLB không thấy = null): chạy lại không tra lại
    qids: dict[str, str | None] = json.loads(cache.read_text()) if cache.exists() else {}
    for n, r in enumerate(todo, 1):
        if r[0] not in qids:
            qids[r[0]] = search(r[1])
        if n % 25 == 0:
            cache.write_text(json.dumps(qids))
            print(f"{n}/{len(todo)} tìm thấy {sum(1 for v in qids.values() if v)}", file=sys.stderr, flush=True)
    cache.write_text(json.dumps(qids))
    found = {k: v for k, v in qids.items() if v and k in {r[0] for r in todo}}
    clubs = claims(sorted(set(found.values())))
    refs = {}
    for q in set(found.values()):
        e = clubs.get(q, {})
        for p in ("P115", "P159", "P131"):
            if (v := first(e, p)) and isinstance(v, dict) and v.get("id"):
                refs.setdefault(q, []).append((p, v["id"]))
    places = claims(sorted({i for lst in refs.values() for _, i in lst}))
    for club, q in found.items():
        venue = city = coord = None
        for p, i in refs.get(q, []):
            e = places.get(i, {})
            c = first(e, "P625")
            if p == "P115":
                venue = label(e)
            elif city is None:
                city = label(e)
            if c and coord is None:
                coord = c
        if coord:
            done[club] = {"qid": q, "venue": venue, "city": city, "lat": round(coord["latitude"], 4), "lon": round(coord["longitude"], 4)}
    dst.write_text(json.dumps(done, ensure_ascii=False, indent=1))
    print(json.dumps({"clubs": len(rows), "with_coords": len(done), "new": len(done) - (len(rows) - len(todo))}))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
