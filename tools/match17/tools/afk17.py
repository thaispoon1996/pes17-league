#!/lsiopy/bin/python3
"""Đo người chơi có đang bấm không (AFK, trận 2 người PES17, PO 18/11). Chạy TRONG pes17-bench (docker exec -i, user abc).

Nối thêm 1 client vào socket tay cầm ảo của selkies (/tmp/selkies_js0.sock = index 0 = đội nhà, js1 = index 1 = đội khách):
selkies gửi mọi sự kiện tay cầm tới mọi client (game là 1 client) → mỗi js_event 8 byte = 1 lần bấm/gạt.
Bàn phím (chỉ vé đội nhà gửi được phím) bắt bằng pynput (XRecord trên DISPLAY :1) → tính cho đội nhà.
stdout mỗi 5 s: {"home": giây từ lần bấm cuối | null, "away": ...} (null = chưa bấm lần nào từ lúc chạy).
"""
import json, os, socket, threading, time

CONFIG_LEN = 1360          # js_config_t selkies gửi khi nối (đo 18/11: Length 1360)
last = {"home": None, "away": None}


def watch(side: str, path: str) -> None:
    while True:
        try:
            s = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            s.connect(path)
            got = 0
            while got < CONFIG_LEN:
                b = s.recv(CONFIG_LEN - got)
                if not b:
                    raise ConnectionError("đóng khi đang nhận cấu hình")
                got += len(b)
            s.sendall(bytes([8]))   # sizeof(long) = 8 (64-bit)
            while True:
                b = s.recv(4096)
                if not b:
                    raise ConnectionError("selkies đóng")
                last[side] = time.monotonic()
        except Exception:
            time.sleep(2)


def keys() -> None:
    try:
        os.environ.setdefault("DISPLAY", ":1")
        from pynput import keyboard
        keyboard.Listener(on_press=lambda k: last.__setitem__("home", time.monotonic())).start()
    except Exception:
        pass


if __name__ == "__main__":
    threading.Thread(target=watch, args=("home", "/tmp/selkies_js0.sock"), daemon=True).start()
    threading.Thread(target=watch, args=("away", "/tmp/selkies_js1.sock"), daemon=True).start()
    keys()
    while True:
        time.sleep(5)
        now = time.monotonic()
        print(json.dumps({k: None if v is None else round(now - v) for k, v in last.items()}), flush=True)
