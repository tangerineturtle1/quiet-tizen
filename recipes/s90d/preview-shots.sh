#!/bin/sh
# Read-only except for screenshots in /tmp: open Home, Apps and Settings in Quiet Home and capture each.
set -u
export XDG_RUNTIME_DIR=/run WAYLAND_DISPLAY=wayland-0
A=com.samsung.tv.bixby-promotion
rm -f /tmp/pv-*.png; cd /tmp
shot() { enlightenment_info -dump_screen -p /tmp -n "pv-$1.png" >/dev/null 2>&1; [ -s "/tmp/pv-$1.png" ] && echo "$1=$(stat -c %s /tmp/pv-$1.png)" || echo "$1=failed"; }
app_launcher -u 5001 -s $A >/dev/null 2>&1; sleep 4; shot home
app_launcher -u 5001 -s $A quiet_page apps >/dev/null 2>&1; sleep 3; shot apps
app_launcher -u 5001 -s $A quiet_page settings >/dev/null 2>&1; sleep 3; shot settings
app_launcher -u 5001 -s $A >/dev/null 2>&1
