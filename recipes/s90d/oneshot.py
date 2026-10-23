#!/usr/bin/env python3
r"""S90D one-shot round: screen sharing, multi-device, sidebar/browser and extras, plus the settings bridge.

Prerequisites (see docs/10-ps2-theme.md):
  1. Quiet Home installed and verified (recipes/install-launcher.sh), Home/startup assigned.
  2. A fresh snapshot pulled and the two plans generated from it:
       python3 tools/tv.py ... exec --script recipes/snapshot.sh --arg oneshot-01
       python3 tools/tv.py ... pull /opt/usr/share/quiet-tizen-backups/oneshot-01/.pkgmgr_parser.db .local/oneshot-01-registry.db
       python3 tools/plan_cleanup.py --profile profiles/qn55s90dafxza-2130.0.json --snapshot .local/oneshot-01-registry.db \
         --groups screen-sharing multi-device --output .local/plans/oneshot-01a
       python3 tools/plan_cleanup.py --profile profiles/qn55s90dafxza-2130.0.json --snapshot .local/oneshot-01-registry.db \
         --groups light-shell shell-extras --output .local/plans/oneshot-01b
     Review apply.sql and restore.sql before continuing.

  python3 recipes/s90d/oneshot.py --host TV_IP --callback-host PC_IP --firmware FW apply
  python3 recipes/s90d/oneshot.py --host TV_IP --callback-host PC_IP --firmware FW restore

apply stops at the first failed step; restore tries every step and reports each one.
Restart the TV afterwards (not inside a root session)."""
import argparse, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "vendor"))
SNAP = "oneshot-01"
S = "recipes/s90d/"
MARKERS = [("com.samsung.tv.airplay-service-2.service", "com.samsung.tv.airplay-service-2"),
           ("com.samsung.tv.googlecast-service.service", "com.samsung.tv.googlecast-service"),
           ("bms.service", "bms")]


def script(path, *args):
    return ["exec", "--script", path] + [x for a in args for x in ("--arg", a)]


def tv_command(a):
    from samsung_tv_root.sdb import find_sdb
    return [sys.executable, str(ROOT / "tools/tv.py"), "--host", a.host, "--firmware", a.firmware,
            "--sdb", a.sdb or str(find_sdb()), "--callback-host", a.callback_host]


APPLY = [("Turn off screen sharing + phone/multi-device apps", script(f".local/plans/{SNAP}a/apply.sh")),
         ("Turn off sidebar, browser, preview updater, aria engine, TV Plus bms", script(f".local/plans/{SNAP}b/apply.sh"))]
APPLY += [(f"Stop marker {m}", script("recipes/stop-marker.sh", "apply", u, m, SNAP)) for u, m in MARKERS]
APPLY += [("Multi View boot preload off", script(S + "multiview-preload.sh", "apply", SNAP)),
          ("Settings buttons -> Quiet Home settings", script(S + "install-settings-bridge-s90d.sh", SNAP))]
RESTORE = [("Sidebar/browser/updater/aria/bms back on", script(f".local/plans/{SNAP}b/restore.sh")),
           ("Screen sharing + phone apps back on", script(f".local/plans/{SNAP}a/restore.sh"))]
RESTORE += [(f"Remove stop marker {m}", script("recipes/stop-marker.sh", "restore", u, m, SNAP)) for u, m in MARKERS]
RESTORE += [("Multi View preload back", script(S + "multiview-preload.sh", "restore", SNAP)),
            ("Original settings buttons + QuickLaunch", script(S + "restore-settings-bridge-s90d.sh", SNAP))]


def run(steps, tv, stop_on_fail, log_name):
    log = open(ROOT / f".local/{log_name}.log", "a", encoding="utf-8")
    results = []
    for title, args in steps:
        print(f"\n=== {title}", flush=True); log.write(f"\n=== {title}\n")
        p = subprocess.run(tv + args, cwd=ROOT, capture_output=True, text=True)
        out = (p.stdout + p.stderr).strip(); print(out); log.write(out + "\n"); log.flush()
        results.append((title, p.returncode == 0))
        if p.returncode and stop_on_fail:
            break
    print("\n=== Summary"); log.write("\n=== Summary\n")
    for title, ok in results:
        line = f"{'OK  ' if ok else 'FAIL'} {title}"; print(line); log.write(line + "\n")
    skipped = len(steps) - len(results)
    if skipped:
        print(f"Stopped early; {skipped} later steps not run. Nothing after the failure was changed.")
    return all(ok for _, ok in results) and not skipped


def parser():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", required=True, help="The TV's IPv4 address")
    p.add_argument("--callback-host", required=True, help="This computer's LAN IPv4")
    p.add_argument("--firmware", required=True, help="Exact version from About This TV")
    p.add_argument("--sdb", help="Path to Samsung's sdb (auto-detected if omitted)")
    return p


def main():
    p = parser(); p.add_argument("action", choices=["apply", "restore"]); a = p.parse_args()
    for plan in ("a", "b"):
        if not (ROOT / f".local/plans/{SNAP}{plan}/apply.sh").is_file():
            p.error(f"Generate .local/plans/{SNAP}{plan} first; see the instructions at the top of this file.")
    ok = run(APPLY if a.action == "apply" else RESTORE, tv_command(a), a.action == "apply", f"oneshot-{a.action}")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
