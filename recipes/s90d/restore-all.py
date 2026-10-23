#!/usr/bin/env python3
r"""Put an S90D back the way it was before quiet-tizen, newest change first.

  python3 recipes/s90d/restore-all.py --host TV_IP --callback-host PC_IP --firmware FW \
      --launcher-snapshot home-01 --main-operation ORIG --home-app ORIG --startup-app ORIG

The three ORIG values are the ones YOUR TV had before Home/startup were changed (recorded in
preferences-before.txt and the Probe output you saved). Steps:
  1. the one-shot round (oneshot.py restore), if it was applied
  2. every other registry plan under .local/plans/, newest first, and each plan's prefs-restore.sh
  3. every stop marker recorded in the TV's backup folders
  4. the original Bixby Promotion app and Home/startup routing
Each step is guarded on the TV; a FAIL usually means it was already undone or never applied.
Restart the TV when it finishes."""
import subprocess, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import oneshot
from oneshot import ROOT, script

S = "recipes/s90d/"


def main():
    p = oneshot.parser()
    for name in ("--launcher-snapshot", "--main-operation", "--home-app", "--startup-app"):
        p.add_argument(name, required=True)
    a = p.parse_args()
    if input("Type RESTORE to put the TV back to stock: ").strip() != "RESTORE":
        sys.exit("Cancelled; nothing changed.")
    tv = oneshot.tv_command(a)
    steps = []
    if (ROOT / f".local/plans/{oneshot.SNAP}a/restore.sh").is_file():
        steps += oneshot.RESTORE
    plans = sorted((d for d in (ROOT / ".local/plans").iterdir()
                    if d.is_dir() and not d.name.startswith(oneshot.SNAP) and (d / "restore.sh").is_file()),
                   key=lambda d: d.stat().st_mtime, reverse=True)
    for d in plans:
        steps.append((f"Registry plan {d.name} back on", script(f".local/plans/{d.name}/restore.sh")))
        if (d / "prefs-restore.sh").is_file():
            steps.append((f"Preferences from {d.name} back", script(f".local/plans/{d.name}/prefs-restore.sh")))
    steps += [("Stop markers removed", script(S + "restore-markers.sh")),
              ("Original Samsung home, startup and Bixby Promotion app",
               script(S + "restore-launcher-s90d.sh", a.launcher_snapshot, a.main_operation, a.home_app, a.startup_app))]
    oneshot.run(steps, tv, False, "restore-all")
    print("\nRestart the TV now. Details: .local/restore-all.log")


if __name__ == "__main__":
    main()
