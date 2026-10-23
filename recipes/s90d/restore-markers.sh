#!/bin/sh
# Remove every profile stop marker that recipes/stop-marker.sh applied, using the record it saved
# in the backup folder. Markers that were never applied are skipped. Run after the registry restores.
set -eu
test "$(id -u)" = 0
B=/opt/usr/share/quiet-tizen-backups
for pair in pisa-service.service:pisa-service canalysis-service.service:canalysis-daemon \
 com.samsung.tv.bixby-prov.service:com.samsung.tv.bixby-prov com.samsung.tv.ondevice-voice.service:com.samsung.tv.ondevice-voice \
 org.tizen.voice-app.service:org.tizen.voice-app org.tizen.voice-client.service:org.tizen.voice-client \
 com.samsung.tv.context-aware-agent.service:com.samsung.tv.context-aware-agent com.samsung.tv.context-aware-service.service:com.samsung.tv.context-aware-service \
 com.samsung.tv.AICompanionAgent.service:com.samsung.tv.AICompanionAgent com.samsung.tv.ghdaemon.service:com.samsung.tv.ghdaemon \
 samsunghealth-service-tv.service:samsunghealth-service-tv slive-provider-daemon.service:slive-provider-daemon \
 com.samsung.tv.airplay-service-2.service:com.samsung.tv.airplay-service-2 com.samsung.tv.googlecast-service.service:com.samsung.tv.googlecast-service \
 bms.service:bms; do
  unit=${pair%%:*}; marker=${pair#*:}; path=/opt/usr/share/systemd/system/$marker; saved=
  for d in "$B"/*; do [ -f "$d/marker-$marker" ] && { saved=$d/marker-$marker; break; }; done
  if [ -z "$saved" ]; then echo "$marker: never applied, skipped"; continue; fi
  test ! -L "$path"
  case "$(cat "$saved")" in absent) rm -f "$path";; present) ;; *) echo "$marker: bad record" >&2; exit 1;; esac
  systemctl reset-failed "$unit" 2>/dev/null || true
  if [ "$(cat "$saved.active" 2>/dev/null)" = active ]; then systemctl start "$unit" || echo "$unit: start failed (will start on restart)"; fi
  echo "$marker: restored from $saved ($(systemctl is-active "$unit" 2>/dev/null))"
done
