"""Vá Selkies (PES17, 24/11): người chơi rớt kết nối khi đang giữ nút/cần → tay cầm ảo bền (persistent slot) giữ nguyên
trạng thái cuối → trong game cầu thủ tự chạy/menu tự chuyển tab mãi. Vá: đóng websocket của client có ô tay cầm → nhả hết nút + cần
của ô đó. Chạy lại nhiều lần không sao (đã vá thì bỏ qua). Dùng: python3 selkies-padfix.py [thư-mục-gói-selkies]"""
import sys
from pathlib import Path

D = Path(sys.argv[1] if len(sys.argv) > 1 else "/lsiopy/lib/python3.12/site-packages/selkies")
MARK = "pes_release_gamepad"


def patch(f: Path, old: str, new: str) -> None:
    s = f.read_text()
    if MARK in s:
        print(f"{f.name}: đã vá")
        return
    if s.count(old) != 1:
        sys.exit(f"{f.name}: không tìm thấy đúng 1 chỗ để vá (selkies đổi phiên bản?)")
    f.write_text(s.replace(old, new))
    print(f"{f.name}: vá xong")


patch(D / "input_handler.py", "    async def __gamepad_disconnect(self, gamepad_idx=None):\n",
      "    async def pes_release_gamepad(self, idx):\n"
      "        g = self.gamepad_instances.get(idx)\n"
      "        if not g:\n"
      "            return\n"
      "        for b in range(17):\n"
      "            g.send_event(b, 0, is_button_event=True)\n"
      "        for a in range(4):\n"
      "            g.send_event(a, 0, is_button_event=False)\n"
      "        logger_webrtc_input.info(f\"PES17: released all inputs of gamepad slot {idx}\")\n\n"
      "    async def __gamepad_disconnect(self, gamepad_idx=None):\n")

patch(D / "selkies.py", "            client_permissions.pop(websocket, None)\n            data_logger.info(f\"Cleaning up Data WS handler",
      "            _pes_perm = client_permissions.pop(websocket, None)\n"
      "            if _pes_perm and _pes_perm.get(\"slot\") and hasattr(self.input_handler, \"pes_release_gamepad\"):\n"
      "                try:\n"
      "                    await self.input_handler.pes_release_gamepad(int(_pes_perm[\"slot\"]) - 1)\n"
      "                except Exception as e_pes:\n"
      "                    data_logger.warning(f\"PES17 release gamepad failed: {e_pes}\")\n"
      "            data_logger.info(f\"Cleaning up Data WS handler")
