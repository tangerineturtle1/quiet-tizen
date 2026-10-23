#!/bin/sh
# Read-only: capture the TV's graphics layer to /tmp/qt-shot.png and cut it into <=600 KB parts for pulling.
set -u
export XDG_RUNTIME_DIR=/run WAYLAND_DISPLAY=wayland-0
rm -f /tmp/qt-shot.png /tmp/qt-shot.part*
cd /tmp
enlightenment_info -dump_screen -p /tmp -n qt-shot.png >/tmp/qt-shot.log 2>&1 || true
[ -s /tmp/qt-shot.png ] || { cat /tmp/qt-shot.log; exit 1; }
size=$(stat -c %s /tmp/qt-shot.png); i=0
while [ $((i * 600000)) -lt "$size" ]; do dd if=/tmp/qt-shot.png of=/tmp/qt-shot.part$i bs=600000 skip=$i count=1 2>/dev/null; i=$((i + 1)); done
echo "parts=$i size=$size"
