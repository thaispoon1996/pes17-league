#!/usr/bin/env python3
"""Đoạn nguồn từng bàn thắng (PO 12/11: web chỉ còn clip tổng hợp bàn thắng của vòng; đoạn này chỉ dùng nội bộ để dựng clip vòng
và bình chọn bàn đẹp). Mỗi bàn: PRE giây trước + POST giây sau mốc bàn trong bản ghi hình, 1280x720 25 fps, tiếng AAC stereo
(bản ghi không tiếng → tiếng im) để round_reel.py ghép không cần đoán định dạng. Bàn đã có file thì bỏ qua.

  goal_clips.py RECORDING.mkv OUTDIR < goals.json      — goals: [{"id": "<goal:N>", "t": giây_trong_bản_ghi}]
  Ghi OUTDIR/{id}.mp4; mốc bàn nằm ở giây PRE của đoạn (đầu bản ghi thì sớm hơn: in ra "at"). In JSON [{id, at, seconds}].
"""
import json, os, subprocess, sys, tempfile

PRE, POST = 12.0, 6.0
IMAGE = 'pes17league-rec:local'

def main(rec, outdir):
    goals = json.load(sys.stdin)
    rec, outdir = os.path.abspath(rec), os.path.abspath(outdir)
    if not os.path.isdir(outdir):   # web chạy root: thư mục mới cho chủ thư mục cha để người dùng host dọn được
        os.makedirs(outdir)
        if os.getuid() == 0: st = os.stat(os.path.dirname(outdir)); os.chown(outdir, st.st_uid, st.st_gid)
    st = os.stat(outdir); uid, gid = st.st_uid, st.st_gid
    out = []
    with tempfile.TemporaryDirectory(dir=outdir) as tmp:
        if os.getuid() == 0: os.chown(tmp, uid, gid)
        run = lambda *a: subprocess.run(['docker', 'run', '--rm', '--cpus', '4', '-v', f'{os.path.dirname(rec)}:/in:ro', '-v', f'{tmp}:/w',
                                         '-u', f'{uid}:{gid}', IMAGE, *a], capture_output=True, text=True)
        src = f'/in/{os.path.basename(rec)}'
        j = json.loads(run('ffprobe', '-v', 'error', '-show_entries', 'format=duration:stream=codec_type', '-of', 'json', src).stdout or '{}')
        dur = float(j.get('format', {}).get('duration', 0))
        has_audio = any(s.get('codec_type') == 'audio' for s in j.get('streams', []))
        if dur <= 0: raise SystemExit('không đọc được bản ghi hình')
        for g in goals:
            dst = os.path.join(outdir, f"{g['id']}.mp4")
            a = max(0.0, g['t'] - PRE); b = min(dur, g['t'] + POST)
            if b - a < 3: continue   # mốc ngoài bản ghi
            if not os.path.exists(dst):
                args = ['-ss', f'{a:.2f}', '-t', f'{b - a:.2f}', '-i', src]
                args += ['-map', '0:v:0', '-map', '0:a:0', '-af', 'aresample=48000,aformat=channel_layouts=stereo'] if has_audio else \
                        ['-f', 'lavfi', '-i', 'anullsrc=r=48000:cl=stereo', '-map', '0:v:0', '-map', '1:a', '-shortest']
                r = run('ffmpeg', '-loglevel', 'error', *args,
                        '-vf', 'scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2,fps=25,setsar=1',
                        '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20', '-pix_fmt', 'yuv420p',
                        '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', '-y', '/w/out.mp4')
                if r.returncode: raise SystemExit(f"ffmpeg lỗi (bàn {g['id']}): {r.stderr.strip()[-300:]}")
                os.replace(os.path.join(tmp, 'out.mp4'), dst)
            out.append({'id': g['id'], 'at': round(g['t'] - a, 2), 'seconds': round(b - a, 2)})
    print(json.dumps(out))

if __name__ == '__main__':
    main(*sys.argv[1:3])
