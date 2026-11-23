#!/lsiopy/bin/python3
"""Inject tay cầm ảo vào Selkies (PES17 spike). Chạy TRONG pes17-bench.

  pad17.py [SLOT]   — mặc định SLOT=0 (P1)
  stdin: chuỗi lệnh cách nhau khoảng trắng, ví dụ:
    A w:1 Down Down A w:2 B
  A/Return=confirm(0)  B/Escape=back(1)  X=2 Y=3
  Up/Down/Left/Right = d-pad 12..15
  w:N = ngủ N giây
  +A / -A = nhấn giữ / nhả nút; aN:V = trục N (0 = cần trái ngang, 1 = dọc) giá trị V (-1..1)
"""
import asyncio, base64, hashlib, os, sys
import websockets

BTN = {
    'A': 0, 'Return': 0, 'Confirm': 0,
    'B': 1, 'Escape': 1, 'Back': 1,
    'X': 2, 'Y': 3,
    'LB': 4, 'RB': 5,
    # Selkies map: 6/7 = LT/RT, 8 = Back, 9 = Start (không phải index 6/7)
    'LT': 6, 'RT': 7,
    'Select': 8, 'Back': 8, 'Start': 9,
    'Up': 12, 'Down': 13, 'Left': 14, 'Right': 15,
}
for i in range(17):
    BTN[f'b{i}'] = i

def url(slot: int) -> str:
    """Chế độ token (18/11): vé pad ô slot+1 = sha256(master + ':pad' + ('' | 'N'))[:32], pes-tokens.py cấp sẵn."""
    mk = os.environ.get('SELKIES_MASTER_TOKEN')
    if not mk:
        return 'ws://127.0.0.1:8082/'
    n = slot + 1
    return 'ws://127.0.0.1:8082/?token=' + hashlib.sha256((mk + ':pad' + ('' if n == 1 else str(n))).encode()).hexdigest()[:32]


async def main(slot: int):
    async with websockets.connect(url(slot), max_size=None, ping_interval=None) as ws:
        async def drain():
            try:
                async for _ in ws:
                    pass
            except Exception:
                pass
        asyncio.get_running_loop().create_task(drain())
        # cắm pad ảo (giống padd.py / browser Standard Gamepad)
        await ws.send(f"js,c,{slot},{base64.b64encode(b'Microsoft X-Box 360 pad').decode()},8,11")
        await asyncio.sleep(1.2)
        print('ready', flush=True)
        loop = asyncio.get_running_loop()
        while True:
            line = await loop.run_in_executor(None, sys.stdin.readline)
            if not line:
                return
            for k in line.split():
                if k.startswith('w:'):
                    await asyncio.sleep(float(k[2:]))
                    continue
                if k.startswith('a') and ':' in k:   # a0:0.5 = cần trái ngang (0 = X, 1 = Y), giá trị -1..1; a0:0 = thả
                    n, v = k[1:].split(':')
                    await ws.send(f'js,a,{slot},{int(n)},{float(v)}')
                    await asyncio.sleep(0.05)
                    continue
                if k[:1] in '+-' and BTN.get(k[1:]) is not None:   # +A = nhấn giữ, -A = nhả (kéo vị trí trong GAME PLAN)
                    await ws.send(f'js,b,{slot},{BTN[k[1:]]},{1 if k[0] == "+" else 0}')
                    await asyncio.sleep(0.15)
                    continue
                b = BTN.get(k)
                if b is None:
                    print('skip', k, flush=True)
                    continue
                await ws.send(f'js,b,{slot},{b},1')
                await asyncio.sleep(0.12)
                await ws.send(f'js,b,{slot},{b},0')
                await asyncio.sleep(0.35)
            print('ok', flush=True)

if __name__ == '__main__':
    asyncio.run(main(int(sys.argv[1]) if len(sys.argv) > 1 else 0))
