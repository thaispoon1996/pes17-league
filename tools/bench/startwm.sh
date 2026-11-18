#!/usr/bin/env bash
# Minimal WM for low-latency PES17 stream (no XFCE panel/desktop).
# Openbox only — less compositor/UI overhead than xfce4-session.

if which nvidia-smi > /dev/null 2>&1 && ls -A /dev/dri 2>/dev/null && [ "${DISABLE_ZINK}" == "false" ]; then
  export LIBGL_KOPPER_DRI2=1
  export MESA_LOADER_DRIVER_OVERRIDE=zink
  export GALLIUM_DRIVER=zink
fi

export XDG_CURRENT_DESKTOP=Openbox
export XDG_SESSION_DESKTOP=openbox
xset s off -dpms 2>/dev/null || true
exec dbus-launch --exit-with-session /usr/bin/openbox
