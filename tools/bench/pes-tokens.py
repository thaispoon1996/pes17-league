#!/lsiopy/bin/python3
"""Cấp vé selkies cho máy PES17 (chế độ token, 18/11). Web gọi: docker exec -i pes17-bench /lsiopy/bin/python3 /usr/local/bin/pes-tokens.py
stdin JSON {"home": "...", "away": "..."}; {} = không có trận → chỉ giữ vé tay cầm ảo của máy (link người chơi cũ hết hiệu lực).
  home = controller ô 1 (màn chính + bàn phím + tay cầm index 0 = P1 đội nhà)
  away = viewer ô 2 (chỉ tay cầm index 1 = P2; máy đưa P2 sang cột AWAY ở màn chọn tay cầm)
  pad  = vé tay cầm ảo của máy cho từng ô N=1..4: sha256(master + ':pad' + ('' nếu N=1, ngược lại str(N)))[:32] — pad17.py tính giống hệt."""
import hashlib, json, os, sys, urllib.request

mk = os.environ['SELKIES_MASTER_TOKEN']
req = json.loads(sys.stdin.read() or '{}')
toks = {hashlib.sha256((mk + ':pad' + ('' if n == 1 else str(n))).encode()).hexdigest()[:32]: {'role': 'viewer', 'slot': n}
        for n in range(1, 5)}
if req.get('home'): toks[req['home']] = {'role': 'controller', 'slot': 1}
if req.get('away'): toks[req['away']] = {'role': 'viewer', 'slot': 2}
r = urllib.request.Request('http://127.0.0.1:8083/tokens', json.dumps(toks).encode(),
                           {'Authorization': 'Bearer ' + mk, 'Content-Type': 'application/json'})
print(urllib.request.urlopen(r, timeout=5).status)
