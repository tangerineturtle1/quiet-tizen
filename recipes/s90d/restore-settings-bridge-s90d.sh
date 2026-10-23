#!/bin/sh
# Undo install-settings-bridge-s90d.sh. Restore the registry plans first.
set -eu
test "$(id -u)" = 0
name=${1:?Settings-bridge snapshot name required}
case "$name" in ''|*[!A-Za-z0-9_-]*) exit 1;; esac
D=/opt/usr/share/quiet-tizen-backups/$name
Q=/opt/usr/apps/com.samsung.tv.quicklaunch
C=/opt/dbspace/.app-context.db
NEW="target_selector='package:com.samsung.tv.bixby-promotion' AND args='quiet_page:settings __K_SPLASH_SCREEN:LA_UI_VR_TRANSPARENT'"
test -s "$D/QuickLaunch-before.dll"
test -s "$D/.app-context.db"
sha256sum -c "$D/installed-bridge.sha256"
sqlite3 -bail "$C" <<SQL
ATTACH '$D/.app-context.db' AS before;
BEGIN IMMEDIATE;
CREATE TEMP TABLE guard(ok INTEGER CHECK(ok=1));
INSERT INTO guard SELECT count(*)=1 FROM key_filter WHERE appid='com.samsung.tv.quicklaunch' AND key='XF86More' AND $NEW;
INSERT INTO guard SELECT count(*)=1 FROM key_filter WHERE appid='org.tizen.menu' AND key='XF86SysMenu' AND $NEW;
INSERT INTO guard SELECT count(*)=1 FROM before.key_filter WHERE appid='com.samsung.tv.quicklaunch' AND key='XF86More' AND target_selector='package:com.samsung.tv.quicklaunch';
INSERT INTO guard SELECT count(*)=1 FROM before.key_filter WHERE appid='org.tizen.menu' AND key='XF86SysMenu' AND target_selector='package:org.tizen.menu';
UPDATE key_filter SET target_selector='package:com.samsung.tv.quicklaunch',
 args=(SELECT args FROM before.key_filter WHERE appid='com.samsung.tv.quicklaunch' AND key='XF86More' AND target_selector='package:com.samsung.tv.quicklaunch')
 WHERE appid='com.samsung.tv.quicklaunch' AND key='XF86More' AND $NEW;
UPDATE key_filter SET target_selector='package:org.tizen.menu',
 args=(SELECT args FROM before.key_filter WHERE appid='org.tizen.menu' AND key='XF86SysMenu' AND target_selector='package:org.tizen.menu')
 WHERE appid='org.tizen.menu' AND key='XF86SysMenu' AND $NEW;
DROP TABLE guard;
COMMIT;
SQL
app_launcher -u 5001 -k com.samsung.tv.quicklaunch || true
cp -a "$D/QuickLaunch-before.dll" "$Q/bin/QuickLaunch.dll"
chsmack -a User::Pkg::com.samsung.tv.quicklaunch::RO "$Q/bin/QuickLaunch.dll"
if [ -d "$D/quicklaunch-native-image" ]; then
    test ! -e "$Q/bin/.native_image"
    cp -a "$D/quicklaunch-native-image" "$Q/bin/.native_image"
fi
vconftool set -f -t int memory/appfw/app_context_db_change 1
echo 'Original settings routing and QuickLaunch restored.'
