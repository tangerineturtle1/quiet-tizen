#!/bin/sh
# Update an installed Quiet Home in place: new assembly and/or overlay of resource files.
# The original Bixby Promotion backup from install (home-01) is never touched.
set -eu
test "$(id -u)" = 0
D=/opt/usr/share/quiet-tizen-backups/home-01
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
S=/home/owner/share/tmp/sdk_tools/quiet-tizen
L=User::Pkg::com.samsung.tv.bixby-promotion::RO
test -d "$D/bixby-promotion-original"
test -d "$A/res/quiet-home"
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
if [ -s "$S/BixbyPromotion.dll" ]; then
  cp "$S/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
  chown 201:0 "$A/bin/BixbyPromotion.dll"; chmod 644 "$A/bin/BixbyPromotion.dll"; chsmack -a "$L" "$A/bin/BixbyPromotion.dll"
  rm -f "$S/BixbyPromotion.dll"; echo "assembly replaced"
fi
if [ -s "$S/overlay.tar.gz" ]; then
  tar -xzf "$S/overlay.tar.gz" -C "$A/res/quiet-home"; rm -f "$S/overlay.tar.gz"; echo "resources updated"
fi
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$L" {} \;
sha256sum "$A/bin/BixbyPromotion.dll"
