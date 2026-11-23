#!/usr/bin/env python3
"""Clip tổng hợp bàn thắng 1 vòng của 1 giải (PO 12/11): CHỈ các pha bàn thắng, không ghép highlight trận, không chiếu chậm.
Mỗi bàn: pha dẫn tới bàn (MAIN_PRE giây trước mốc bàn, MAIN_POST sau) + chữ "phút' người ghi · tỷ số".
Mở đầu khung tên giải/vòng, kết thúc khung outro.
Ra bản 16:9 và bản dọc 9:16 (video giữa khung, nền là chính video phóng to làm mờ). Nguồn: đoạn từng bàn của goal_clips.py.

  round_reel.py ROOT OUT.mp4 OUT_DOC.mp4 < spec.json
  ROOT: thư mục dữ liệu trên host (docker -v ROOT:/w); OUT, OUT_DOC, src là đường dẫn tương đối với ROOT.
  spec: {"title": "Premier League", "subtitle": "Vòng 5 · 23 bàn", "outro": ["Hẹn gặp lại ở vòng sau", "PES 17 League"],
         "items": [{"src": "goals/<id>.mp4", "at": 12.0, "who": "23' Saka", "score": "Arsenal 1–0 Chelsea"}]}
  at = giây của mốc bàn trong src. Ảnh đại diện: OUT bỏ .mp4 + .jpg. In JSON {path, doc, poster, seconds, marks: [giây bắt đầu từng bàn]}.
  Tuỳ chọn (trang /admin/do-hoa, PO 23/11): "logo": "logos/x.png" — logo giải trên khung mở đầu;
  "bug": {"src": "graphics/bug.png", "corner": "tl|tr|bl|br", "size": % chiều rộng khung, "opacity": %} — logo kênh in góc mọi pha bàn thắng.
  File không có thì bỏ qua.
"""
import json, os, subprocess, sys, tempfile

IMAGE = 'pes17league-rec:local'
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
INTRO, OUTRO = 2.5, 3.0
MAIN_PRE, MAIN_POST = 8.0, 3.0   # PO 17/11: cắt gọn (trước 8/5)
FADE = 0.25

def fit(s, size, w):
    """Cỡ chữ để dòng s (DejaVu Bold, ~0,62 cỡ/ký tự) lọt 92% bề ngang w."""
    return max(18, min(size, int(w * 0.92 / (0.62 * max(1, len(s))))))
BG, ACCENT = '0x0f131d', '0x00ff87'   # PES17: giao diện xanh (PO 22/11), không dùng navy/vàng của PES6
# docker --cpus 4: dựng nền, không giành CPU của web/máy trận
ENCODE = ['-c:v', 'libx264', '-preset', 'veryfast', '-crf', '21', '-pix_fmt', 'yuv420p', '-r', '25',
          '-c:a', 'aac', '-b:a', '128k', '-ar', '48000', '-ac', '2']

def main(root, out, out_doc):
    spec = json.load(sys.stdin)
    root = os.path.abspath(root)
    if not spec.get('items'): raise SystemExit('vòng không có bàn nào có video')
    st = os.stat(root); uid, gid = st.st_uid, st.st_gid
    d = os.path.join(root, os.path.dirname(out))
    if not os.path.isdir(d):   # web chạy root: thư mục mới cho chủ ROOT để người dùng host dọn được
        os.makedirs(d)
        if os.getuid() == 0: os.chown(d, uid, gid)
    with tempfile.TemporaryDirectory(dir=os.path.join(root, os.path.dirname(out))) as tmp:
        if os.getuid() == 0: os.chown(tmp, uid, gid)
        t = '/w/' + os.path.relpath(tmp, root)
        run = lambda *a: subprocess.run(['docker', 'run', '--rm', '--cpus', '4', '-v', f'{root}:/w', '-u', f'{uid}:{gid}', IMAGE, *a],
                                        capture_output=True, text=True)
        n_txt = [0]

        def duration(path):
            r = run('ffprobe', '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', path)
            return float(r.stdout.strip() or 0)

        def text(s, size, x, y, color='white', box='black@0.6'):
            n_txt[0] += 1
            name = f't{n_txt[0]}.txt'
            with open(os.path.join(tmp, name), 'w') as f: f.write(s)
            return (f",drawtext=fontfile={FONT}:textfile={t}/{name}:fontsize={size}:fontcolor={color}:box=1:boxcolor={box}:boxborderw=12"
                    f":x={x}:y={y}")

        def encode(name, args, fc):
            r = run('ffmpeg', '-loglevel', 'error', *args, '-filter_complex', ';'.join(fc), '-map', '[v]', '-map', '[a]', *ENCODE,
                    '-f', 'mpegts', '-y', f'{t}/{name}.ts')
            if r.returncode: raise SystemExit(f'ffmpeg lỗi ({name}): {r.stderr.strip()[-300:]}')
            return f'{name}.ts'

        def card(name, lines, sec, w, h, logo=None):
            """Khung chữ giữa màn nền tối: [(chữ, cỡ, màu)]; logo (đường dẫn trong ROOT) phía trên chữ."""
            gap = 1.6
            lh = round(min(w, h) * 0.2) if logo else 0   # cao logo
            block = sum(size * gap for _, size, _ in lines) + (lh + 28 if logo else 0)
            y = h / 2 - block / 2
            top = y
            if logo: y += lh + 28
            draw = ''
            for s, size, color in lines:
                if s: draw += text(s, fit(s, size, w), '(w-text_w)/2', f'{y:.0f}', color=color, box=f'{BG}@0')
                y += size * gap
            # nguồn lavfi phải có -t: nguồn vô hạn làm ffmpeg chạy mãi
            args = ['-f', 'lavfi', '-t', f'{sec}', '-i', f'color=c={BG}:s={w}x{h}:r=25', '-f', 'lavfi', '-t', f'{sec}', '-i', 'anullsrc=r=48000:cl=stereo']
            base = '[0:v]'
            pre = []
            if logo:
                args += ['-i', f'/w/{logo}']
                pre = [f'[2:v]scale=-2:{lh},format=rgba[lg]', f'[0:v][lg]overlay=(W-w)/2:{top:.0f}[c0]']
                base = '[c0]'
            return encode(name, args, pre + [f'{base}format=yuv420p,fade=in:st=0:d={FADE},fade=out:st={sec - FADE}:d={FADE}{draw},setsar=1[v]', '[1:a]anull[a]'])

        bug = spec.get('bug') if spec.get('bug') and os.path.isfile(os.path.join(root, spec['bug']['src'])) else None
        logo = spec.get('logo') if spec.get('logo') and os.path.isfile(os.path.join(root, spec['logo'])) else None

        def stamp(fc, idx, w, h):
            """Logo kênh lên khung cuối [v] của fc (ảnh là đầu vào số idx). Góc dưới chừa dòng chữ người ghi (ngang) / khung dọc."""
            if not bug: return fc
            bw = max(16, round(w * min(20, max(4, int(bug.get('size', 9)))) / 100)) // 2 * 2
            op = min(100, max(20, int(bug.get('opacity', 85)))) / 100
            mx, my = round(w * 0.03), round(h * 0.04)
            c = bug.get('corner', 'tr')
            x = mx if c in ('tl', 'bl') else f'W-w-{mx}'
            y = my if c in ('tl', 'tr') else f'H-h-{64 if h > w else 110}'
            assert fc[-1].endswith('[v]')
            return fc[:-1] + [fc[-1][:-3] + '[vb]', f'[{idx}:v]scale={bw}:-2,format=rgba,colorchannelmixer=aa={op:.2f}[bug]',
                              f'[vb][bug]overlay={x}:{y}:format=auto,format=yuv420p[v]']

        def goal(name, it, w, h):
            dur = duration(f"/w/{it['src']}")
            at = it['at']
            a, b = max(0.0, at - MAIN_PRE), min(dur, at + MAIN_POST)
            main = b - a
            fc = [f'[0:v]setpts=PTS-STARTPTS,fps=25,fade=in:st=0:d={FADE},fade=out:st={main - FADE:.3f}:d={FADE}[cv]',
                  f'[0:a]asetpts=PTS-STARTPTS,afade=in:st=0:d={FADE},afade=out:st={main - FADE:.3f}:d={FADE}[a]']
            if h > w:   # dọc: video 16:9 giữa khung, nền mờ; tỷ số phía trên, người ghi phía dưới
                fc.append(f'[cv]split=2[x][y];[x]scale=180:320:force_original_aspect_ratio=increase,crop=180:320,boxblur=8:2,scale={w}:{h},'
                          f'eq=brightness=-0.15[bg];[y]scale={w}:-2[fg];[bg][fg]overlay=0:(H-h)/2'
                          + text(it['score'], fit(it['score'], 40, w), '(w-text_w)/2', 290)
                          + text(it['who'], fit(it['who'], 48, w), '(w-text_w)/2', 'h-300') + ',setsar=1[v]')
            else:
                line = f"{it['who']}  ·  {it['score']}"
                fc.append('[cv]null' + text(line, fit(line, 32, w - 80), 40, 'h-text_h-52') + ',setsar=1[v]')
            args = ['-ss', f'{a:.3f}', '-t', f'{main:.3f}', '-i', f"/w/{it['src']}"] + (['-i', f"/w/{bug['src']}"] if bug else [])
            return encode(name, args, stamp(fc, 1, w, h))

        def build(w, h, tag):
            big, small = 64, (40 if h > w else 36)
            parts = [card(f'{tag}intro', [(spec['title'], big, 'white'), (spec.get('subtitle', ''), small, ACCENT), ('PES 17 League', small - 8, 'white')],
                          INTRO, w, h, logo)]
            parts += [goal(f'{tag}{i:03d}', it, w, h) for i, it in enumerate(spec['items'])]
            if spec.get('outro'):
                parts.append(card(f'{tag}outro', [(s, big - 8 if k == 0 else small, 'white' if k == 0 else ACCENT) for k, s in enumerate(spec['outro'])],
                                  OUTRO, w, h))
            with open(os.path.join(tmp, f'{tag}.txt'), 'w') as f: f.writelines(f"file '{p}'\n" for p in parts)
            r = run('ffmpeg', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', f'{t}/{tag}.txt', '-c', 'copy',
                    '-bsf:a', 'aac_adtstoasc', '-movflags', '+faststart', '-y', f'{t}/{tag}.mp4')
            if r.returncode: raise SystemExit(f'ffmpeg nối lỗi: {r.stderr.strip()[-300:]}')
            marks, pos = [], duration(f'{t}/{parts[0]}')
            for p in parts[1:1 + len(spec['items'])]:
                marks.append(round(pos, 2)); pos += duration(f'{t}/{p}')
            return marks

        marks = build(1280, 720, 'n')
        build(720, 1280, 'd')
        seconds = duration(f'{t}/n.mp4')
        at = marks[0] + min(MAIN_PRE, spec['items'][0]['at']) + 0.3   # ngay sau khi bóng vào lưới của bàn đầu
        run('ffmpeg', '-loglevel', 'error', '-ss', f'{at:.2f}', '-i', f'{t}/n.mp4', '-frames:v', '1', '-q:v', '3', '-y', f'{t}/poster.jpg')
        # thư mục đích có thể của root: ghi trong tmp rồi chuyển
        os.replace(os.path.join(tmp, 'n.mp4'), os.path.join(root, out))
        os.replace(os.path.join(tmp, 'd.mp4'), os.path.join(root, out_doc))
        poster = os.path.splitext(out)[0] + '.jpg'
        if os.path.exists(os.path.join(tmp, 'poster.jpg')): os.replace(os.path.join(tmp, 'poster.jpg'), os.path.join(root, poster))
    print(json.dumps({'path': out, 'doc': out_doc, 'poster': poster, 'seconds': round(seconds, 1), 'marks': marks}))

if __name__ == '__main__':
    main(*sys.argv[1:3 + 1])
