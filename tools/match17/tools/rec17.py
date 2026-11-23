"""Ghi hình + phát trực tiếp trận PES17 (C7, chuyển từ tools/match/pesmatch.py của PES6: rec_start / live_tee / pulse_host).

Container ffmpeg (pes17league-rec:local, tools/rec) dùng chung mạng với pes17-bench (--network container:) → nối màn X :1
(Xvfb -ac, socket abstract) + tiếng game qua socket Pulse thứ 2 trong /config. Không sửa image máy game, không đụng luồng Selkies.
720p 30 khung/s, x264 veryfast CRF 20 (PES6 dùng 16: ~1,1 GB/trận BOT đo 23/11; 20 ≈ nửa dung lượng, đoạn bàn thắng vốn mã lại CRF 20), GOP 60 = 2 s (khớp đoạn HLS 2 s). Tiếng AAC 128k từ output.monitor (game có tiếng từ 23/11).
Lỗi chỉ báo sự kiện recording_error / live_error / audio_error, trận vẫn đá.

Bật/tắt (PO quyết, mặc định TẮT): file $DATA/pes17-deploy/record-mode = off | hot | live | bot | all
  hot (PO 23/11 chọn): chỉ trận cầu đinh — web truyền PESMATCH_HOT=1 (HotMatchService: 2 đội top 4 BXH / top 4 sức mạnh, bán kết, chung kết).
  live: như hot + web đá thật 1 trận cầu đinh BOT mỗi khung career để phát trực tiếp (MatchJobService.HotLive; người chơi chờ trận đó).
  bot: chỉ trận máy đá với máy (không ai chơi → không giành CPU của người chơi); all: mọi trận.
  PESMATCH_REC_MODE (biến môi trường) đè file — chỉ để thử.
"""
from __future__ import annotations

import os, shutil, subprocess, time
from pathlib import Path
from typing import Callable

IMAGE = "pes17league-rec:local"
MIN_FREE = 30 << 30  # ổ ghi hình còn < 30 GB → không ghi (web ngừng đá BOT khi < 20 GB: MatchOrchestratorOptions.MinFreeDiskBytes)
MAX_SECONDS = 5400   # chặn trên 90 phút: bộ chạy bị giết ngang (không kịp stop) thì ffmpeg vẫn tự dừng, không ghi đầy đĩa
PULSE = ["-u", "abc", "-e", "PULSE_RUNTIME_PATH=/defaults"]   # Pulse của Selkies trong pes17-bench (socket /defaults/native)


def mode(deploy_dir: Path) -> str:
    m = os.environ.get("PESMATCH_REC_MODE")
    if m is None:
        try:
            m = (deploy_dir / "record-mode").read_text()
        except OSError:
            m = "off"
    m = m.strip().lower()
    return m if m in ("hot", "live", "bot", "all") else "off"


def wanted(deploy_dir: Path, human: str | None) -> bool:
    m = mode(deploy_dir)
    return m == "all" or (m == "bot" and human is None) or (m in ("hot", "live") and os.environ.get("PESMATCH_HOT") == "1")


def _exec(c: str, cmd: str) -> subprocess.CompletedProcess:
    return subprocess.run(["docker", "exec", *PULSE, c, "sh", "-c", cmd], capture_output=True, text=True, timeout=30)


def pulse_host(c: str, emit: Callable[..., None]) -> str | None:
    """Socket Pulse /config/pulse-native (auth-anonymous) để container ghi hình đọc tiếng. Trả thư mục host của /config; lỗi → None (ghi không tiếng)."""
    try:
        # Selkies tạo lại có thể chỉ còn auto_null (PES6 11/11) → thiếu "output" thì ffmpeg chết; tạo lại cho chắc
        _exec(c, "for s in output input; do pactl list short sinks | cut -f2 | grep -qx $s "
                 "|| pactl load-module module-null-sink sink_name=$s sink_properties=device.description=$s; done")
        r = _exec(c, "printf 'enable-shm = no\\n' > /config/pulse-client.conf; "
                     "pactl list short modules | grep -q 'socket=/config/pulse-native' "
                     "|| pactl load-module module-native-protocol-unix socket=/config/pulse-native auth-anonymous=1; "
                     "test -S /config/pulse-native")
        if r.returncode:
            emit(event="audio_error", reason=(r.stderr or r.stdout or "pulse")[:150])
            return None
        src = subprocess.run(["docker", "inspect", "-f", '{{range .Mounts}}{{if eq .Destination "/config"}}{{.Source}}{{end}}{{end}}', c],
                             capture_output=True, text=True, timeout=30).stdout.strip()
        if not src:
            emit(event="audio_error", reason="no /config mount")
            return None
        return src
    except (OSError, subprocess.SubprocessError) as e:
        emit(event="audio_error", reason=str(e)[:150])
        return None


def live_tee(live: str, owner: os.stat_result, f: str, audio: bool, emit: Callable[..., None]) -> tuple[list[str], list[str]] | None:
    """Tee: cùng bản mã hoá ra .mkv + HLS {thư mục}/{trận}.m3u8 (đoạn 2 s, giữ 6, xoá đoạn cũ; web: LiveStore /truc-tiep).
    Nhánh HLS lỗi → onfail=ignore, .mkv vẫn ghi. Lỗi thư mục → None (chỉ ghi .mkv)."""
    d, n = os.path.split(os.path.abspath(live))
    n = os.path.splitext(n)[0]
    try:
        os.makedirs(d, exist_ok=True)
        try:
            os.chown(d, owner.st_uid, owner.st_gid)   # ffmpeg chạy bằng chủ thư mục ghi hình
        except OSError:
            pass
        for x in os.listdir(d):                       # chỉ giữ trận đang ghi
            if x.endswith((".m3u8", ".ts", ".tmp")):
                os.remove(os.path.join(d, x))
    except OSError as e:
        emit(event="live_error", reason=str(e)[:150])
        return None
    maps = ["-map", "0:v"] + (["-map", "1:a"] if audio else [])
    return (["-v", f"{d}:/live"],
            ["-y", "-flags", "+global_header", *maps, "-f", "tee",
             f"[f=matroska]/out/{f}|[f=hls:bsfs/v=dump_extra:onfail=ignore:hls_time=2:hls_list_size=6:hls_flags=delete_segments+temp_file"
             f":hls_segment_filename=/live/{n}-%05d.ts]/live/{n}.m3u8"])


def start(c: str, path: str, live: str | None, emit: Callable[..., None]) -> tuple[str | None, float | None]:
    """Bắt đầu ghi. Trả (tên container ghi, mốc 0 của video theo time.monotonic()); lỗi → (None, None).
    Mốc 0 ≈ lúc container chạy (ffmpeg khởi động < 1 s); không chờ file có dữ liệu (muxer đệm vài giây → lệch ~5 s, đo PES6 04/11)."""
    try:
        d, f = os.path.split(os.path.abspath(path))
        os.makedirs(d, exist_ok=True)
        if (free := shutil.disk_usage(d).free) < MIN_FREE:
            emit(event="recording_error", reason=f"ổ ghi hình còn {free >> 30} GB < {MIN_FREE >> 30} GB")
            return None, None
        st, name = os.stat(d), f"pesrec-{c}"
        subprocess.run(["docker", "rm", "-f", name], capture_output=True, timeout=30)
        tap = pulse_host(c, emit)
        tee = live_tee(live, st, f, bool(tap), emit) if live else None
        vol, out = tee or ([], ["-y", f"/out/{f}"])
        pre = (["-e", "PULSE_SERVER=unix:/pulse/pulse-native", "-e", "PULSE_CLIENTCONFIG=/pulse/client.conf",
                "-v", f"{tap}/pulse-native:/pulse/pulse-native", "-v", f"{tap}/pulse-client.conf:/pulse/client.conf:ro"] if tap else [])
        ain = ["-thread_queue_size", "1024", "-f", "pulse", "-i", "output.monitor"] if tap else []
        aenc = ["-c:a", "aac", "-b:a", "128k", "-ac", "2"] if tap else []
        r = subprocess.run(["docker", "run", "-d", "--rm", "--name", name, "--network", f"container:{c}", "-u", f"{st.st_uid}:{st.st_gid}",
                            "-v", f"{d}:/out", *vol, *pre, IMAGE, "ffmpeg", "-nostdin", "-loglevel", "error",
                            "-thread_queue_size", "1024", "-f", "x11grab", "-framerate", "30", "-video_size", "1280x720", "-i", ":1",
                            *ain, "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-g", "60", "-pix_fmt", "yuv420p", *aenc, "-t", str(MAX_SECONDS), *out],
                           capture_output=True, text=True, timeout=60)
        t0 = time.monotonic()
        if r.returncode:
            emit(event="recording_error", reason=r.stderr.strip()[:150])
            return None, None
        emit(event="recording", path=os.path.abspath(path))
        if tee:
            emit(event="live", path=os.path.abspath(live))
        return name, t0
    except (OSError, subprocess.SubprocessError) as e:
        emit(event="recording_error", reason=str(e)[:150])
        return None, None


def stop(name: str | None) -> None:
    """SIGTERM → ffmpeg đóng .mkv gọn (chờ tối đa 20 s)."""
    if name:
        subprocess.run(["docker", "stop", "-t", "20", name], capture_output=True, timeout=60)
