"""Ghép cầu thủ DB với bộ transfermarkt-datasets (PO 22/11, phương án C). Dùng: match.py THU_MUC (có players.csv.gz + db.csv, xem run.sh).
Ra: THU_MUC/matched.csv (mọi cầu thủ + độ tin) và THU_MUC/real-values.csv (chỉ 'high': player_id,value_eur,ref) cho CLI import-real-values.
Khoá ghép: họ (từ cuối tên) bắt buộc + chữ cái đầu tên; điểm cộng tuổi ±1, chiều cao ±2 cm, CLB trùng từ. 'high' = điểm ≥ 4 và hơn người thứ hai ≥ 2.
Bỏ đội Classics (huyền thoại, không có giá thật)."""
import csv, gzip, sys, unicodedata, re, collections, datetime
D = sys.argv[1]
def norm(s):
    s = unicodedata.normalize('NFKD', s or '').encode('ascii','ignore').decode().lower()
    return re.sub(r'[^a-z ]+',' ', s.replace('-', ' ')).split()
GEN = set('fc cf sc ac afc club football de sporting futbol calcio sv fk sk sad cd ssc as us rc ca the s p a spa sa vfb vfl tsg bv bsc og ud real atletico'.split()) - {'real','atletico','sporting'}
def ctok(s): return set(t for t in norm(s) if t not in GEN and len(t)>1)
REF = datetime.date(2025, 9, 1)
tm = []
with gzip.open(f'{D}/players.csv.gz','rt') as f:
    for r in csv.DictReader(f):
        if not r['last_season'] or int(r['last_season']) < 2024 or not r['market_value_in_eur']: continue
        dob = r['date_of_birth'][:10]
        age = None
        if dob:
            b = datetime.date.fromisoformat(dob); age = REF.year - b.year - ((REF.month, REF.day) < (b.month, b.day))
        tm.append(dict(id=r['player_id'], name=r['name'], first=norm(r['first_name']), last=norm(r['last_name']), full=norm(r['name']),
                       age=age, h=int(r['height_in_cm']) if r['height_in_cm'] else None, club=ctok(r['current_club_name']),
                       clubname=r['current_club_name'], val=int(r['market_value_in_eur']), pos=r['position']))
idx = collections.defaultdict(list)
for i,t in enumerate(tm):
    for tok in set(t['full']): idx[tok].append(i)
out = []; stats = collections.Counter()
with open(f'{D}/db.csv') as f:
    for r in csv.DictReader(f):
        toks = norm(r['Name'])
        if not toks: continue
        clas = 'classic' in (r['club'] or '').lower()
        if clas: stats['classics'] += 1; continue
        initials = [t for t in toks if len(t)==1]; words = [t for t in toks if len(t)>1]
        if not words: stats['noname'] += 1; continue
        cands = set(idx[words[-1]]) if words else set()
        age = int(r['age']) if r['age'] else None; h = int(r['h']) if r['h'] else None
        ct = ctok(r['club'] or '')
        scored = []
        for i in cands:
            t = tm[i]; s = 0
            if not all(w in t['full'] for w in words): continue
            if initials:
                if not t['first'] or t['first'][0][0] != initials[0]: continue
                s += 1
            if len(words) >= 2: s += 1
            if age is not None and t['age'] is not None:
                d = abs(age - t['age'])
                if d <= 1: s += 2
                elif d >= 3: s -= 3
            if h and t['h']:
                d = abs(h - t['h'])
                if d <= 2: s += 2
                elif d >= 6: s -= 2
            if ct and t['club'] and ct & t['club']: s += 3
            scored.append((s, i))
        scored.sort(reverse=True)
        if not scored: stats['nomatch'] += 1; out.append((r, None, None)); continue
        best, i = scored[0]
        second = scored[1][0] if len(scored) > 1 else -99
        if best >= 4 and best - second >= 2: conf = 'high'
        elif best >= 3 and best > second: conf = 'mid'
        else: conf = 'low'
        stats[conf] += 1
        out.append((r, tm[i] if conf != 'low' else None, conf))
print(dict(stats), 'total', sum(stats.values()))
with open(f'{D}/matched.csv','w',newline='') as f:
    w = csv.writer(f); w.writerow(['id','name','pos','club','age','ovr','price','conf','tm_id','tm_name','tm_club','tm_age','tm_val'])
    for r, t, c in out:
        w.writerow([r['Id'], r['Name'], r['Position'], r['club'], r['age'], r['Overall'], r['Price'], c or 'none',
                    t['id'] if t else '', t['name'] if t else '', t['clubname'] if t else '', t['age'] if t else '', t['val'] if t else ''])
with open(f'{D}/real-values.csv','w',newline='') as f:
    w = csv.writer(f, lineterminator='\n'); w.writerow(['player_id','value_eur','ref'])
    for r, t, c in out:
        if c == 'high' and t and t['val'] > 0: w.writerow([r['Id'], t['val'], f"tm:{t['id']}"])
