"""Exercise the packaged GUI without network calls or real login credentials."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys


def smoke(executable: Path, root: Path) -> None:
    executable = executable.resolve(strict=True)
    root = root.resolve()
    if root.exists() and any(root.iterdir()):
        raise ValueError("GUI smoke requires an empty data directory.")
    subprocess.run([str(executable), "--ui-smoke"], cwd=executable.parent,
                   env=dict(os.environ, TIPCL_HOME=str(root)), timeout=60, check=True, capture_output=True)
    report = json.loads((root / "ui-smoke.json").read_text(encoding="utf-8"))
    if not report.get("ok") or not all((root / f"ui-{width}.png").is_file() for width in (800, 1060)):
        raise ValueError("GUI smoke did not produce its report and both screenshots.")
    print(json.dumps(report))


if __name__ == "__main__":
    try:
        smoke(Path(sys.argv[1]), Path(sys.argv[2]))
    except (OSError, ValueError, KeyError, IndexError, subprocess.SubprocessError) as error:
        print(f"GUI smoke failed: {error}", file=sys.stderr)
        raise SystemExit(1)
