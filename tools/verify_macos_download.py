"""Inspect an actual browser-downloaded app without changing quarantine or Gatekeeper."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import sys

from smoke_portable import smoke


def inspect(app: Path, restart: bool) -> dict:
    if sys.platform != "darwin":
        raise ValueError("This check must run on the real Mac being tested.")
    app = app.resolve(strict=True)
    host = app / "Contents/MacOS/HelL.Desktop"
    if app.suffix != ".app" or not host.is_file():
        raise ValueError("Select the extracted HelL.app bundle.")
    if "AppTranslocation" in app.parts:
        raise ValueError("Move the full extracted folder to a writable location before testing.")

    def command(*args: str, allowed: tuple[int, ...] = (0,)) -> dict:
        check = subprocess.run(args, capture_output=True, text=True, timeout=45)
        if check.returncode not in allowed:
            raise RuntimeError(f"{args[0]} failed ({check.returncode}): {check.stderr.strip()}")
        return {"exitCode": check.returncode, "output": (check.stdout + check.stderr).strip()}

    report = {"platform": platform.platform(), "machine": platform.machine(),
              "app": str(app), "hostSha256": hashlib.sha256(host.read_bytes()).hexdigest(),
              "dataDirectory": str(app.parent / "data"), "manualFirstOpenVerified": False,
              "signature": command("codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app)),
              "identity": command("codesign", "-dv", "--verbose=2", str(app)),
              "gatekeeper": command("spctl", "--assess", "--type", "execute", "--verbose=4", str(app), allowed=(0, 1, 2, 3)),
              "attributes": command("xattr", str(app))}
    report["quarantinePresent"] = "com.apple.quarantine" in report["attributes"]["output"].splitlines()
    if restart:
        smoke(host)
        report["isolatedRestartVerified"] = True
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--check-restart", action="store_true")
    args = parser.parse_args()
    try:
        if args.report.exists():
            raise FileExistsError("Use a new report filename to preserve previous evidence.")
        report = inspect(args.app, args.check_restart)
        args.report.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"Saved {args.report}. Finder first-open remains a manual verification.")
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"macOS inspection failed: {error}", file=sys.stderr)
        raise SystemExit(1)
