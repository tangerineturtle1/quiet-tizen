#!/bin/sh
# Multi View's own boot switch: its two systemd units skip when disable_preloading exists.
# apply|restore SNAPSHOT_NAME. Records whether the flag was already there.
set -eu
test "$(id -u)" = 0
action=${1:?}; name=${2:?}
case "$name" in ''|*[!A-Za-z0-9_-]*) exit 1;; esac
D=/opt/usr/share/quiet-tizen-backups/$name
F=/home/owner/apps_rw/com.samsung.tv.multiscreen/data/disable_preloading
test -s "$D/.pkgmgr_parser.db"
test -d "$(dirname "$F")"
test ! -L "$F"
saved=$D/flag-multiview-disable_preloading
case "$action" in
 apply) test ! -e "$saved"; if [ -e "$F" ]; then echo present; else echo absent; fi > "$saved"; touch "$F";;
 restore) test -f "$saved"; if [ "$(cat "$saved")" = absent ]; then rm -f "$F"; fi;;
 *) exit 1;;
esac
ls -l "$F" 2>/dev/null || echo 'flag absent'
