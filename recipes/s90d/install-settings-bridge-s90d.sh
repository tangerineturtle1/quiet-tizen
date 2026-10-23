#!/bin/sh
# S90D variant of recipes/install-settings-bridge.sh. This TV has two XF86More rules
# (QuickLaunch itself, plus a background function-osd preload), so only the QuickLaunch
# rule and the Settings-key rule are rerouted; the function-osd rule is left alone.
# Run with a NEW snapshot name that recipes/snapshot.sh just created.
set -eu
test "$(id -u)" = 0
name=${1:?Snapshot name required}
case "$name" in ''|*[!A-Za-z0-9_-]*) exit 1;; esac
D=/opt/usr/share/quiet-tizen-backups/$name
Q=/opt/usr/apps/com.samsung.tv.quicklaunch
S=/home/owner/share/tmp/sdk_tools/quiet-tizen/unpacked
C=/opt/dbspace/.app-context.db
SEL="(appid='com.samsung.tv.quicklaunch' AND key='XF86More' AND target_selector='package:com.samsung.tv.quicklaunch') OR (appid='org.tizen.menu' AND key='XF86SysMenu' AND target_selector='package:org.tizen.menu')"
test -s "$D/.app-context.db"
test -s "$S/QuickLaunch.dll"
test -s "$Q/bin/QuickLaunch.dll"
test "$(stat -c %u:%g "$Q/bin/QuickLaunch.dll")" = 201:0
test ! -e "$D/QuickLaunch-before.dll"
test "$(sqlite3 -readonly "$C" "SELECT count(*) FROM key_filter WHERE $SEL;")" = 2
test "$(sqlite3 -readonly "$D/.app-context.db" "SELECT count(*) FROM key_filter WHERE $SEL;")" = 2
cp -a "$Q/bin/QuickLaunch.dll" "$D/QuickLaunch-before.dll"
app_launcher -u 5001 -k com.samsung.tv.quicklaunch || true
if [ -d "$Q/bin/.native_image" ]; then mv "$Q/bin/.native_image" "$D/quicklaunch-native-image"; fi
cp "$S/QuickLaunch.dll" "$Q/bin/QuickLaunch.dll"
chown 201:0 "$Q/bin/QuickLaunch.dll"
chmod 644 "$Q/bin/QuickLaunch.dll"
chsmack -a User::Pkg::com.samsung.tv.quicklaunch::RO "$Q/bin/QuickLaunch.dll"
sha256sum "$Q/bin/QuickLaunch.dll" > "$D/installed-bridge.sha256"
sqlite3 -bail "$C" <<SQL
BEGIN IMMEDIATE;
CREATE TEMP TABLE guard(ok INTEGER CHECK(ok=1));
INSERT INTO guard SELECT count(*)=2 FROM key_filter WHERE $SEL;
UPDATE key_filter SET target_selector='package:com.samsung.tv.bixby-promotion',args='quiet_page:settings __K_SPLASH_SCREEN:LA_UI_VR_TRANSPARENT' WHERE $SEL;
DROP TABLE guard;
COMMIT;
SQL
vconftool set -f -t int memory/appfw/app_context_db_change 1
sqlite3 -readonly "$C" "SELECT appid,key,target_selector,args FROM key_filter WHERE (appid='com.samsung.tv.quicklaunch' AND key='XF86More') OR (appid='org.tizen.menu' AND key='XF86SysMenu');"
echo 'Settings routing now opens Quiet Home settings.'
