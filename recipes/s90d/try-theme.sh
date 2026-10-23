#!/bin/sh
# Switch Quiet Home to one of its shipped test themes, relaunch it, and report what it did.
set -eu
test "$(id -u)" = 0
A=/opt/usr/apps/com.samsung.tv.bixby-promotion; R=$A/res/quiet-home
v=${1:?theme variant}; case "$v" in *[!a-z-]*) exit 1;; esac
test -f "$R/theme-$v.json"
cp "$R/theme-$v.json" "$R/theme.json"
chown 201:0 "$R/theme.json"; chmod 644 "$R/theme.json"; chsmack -a User::Pkg::com.samsung.tv.bixby-promotion::RO "$R/theme.json"
rm -f /tmp/quiet-home-state.txt
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
sleep 1
app_launcher -u 5001 -s com.samsung.tv.bixby-promotion
sleep 14
echo "== theme $v"
echo '== process'; ps -eo pid,etime,args | grep -i bixby-promotion | grep -v grep || echo 'not running'
echo '== state'; cat /tmp/quiet-home-state.txt 2>/dev/null || echo 'no state file'
echo '== kernel'; dmesg 2>/dev/null | grep -i -E 'segfault|BixbyPromot|dotnet|killed process|oom|lowmem' | tail -8
