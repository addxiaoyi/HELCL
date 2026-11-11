"""Run a published native executable twice with isolated portable state and await both exits."""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def smoke(executable: Path) -> None:
    executable = executable.resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="hell-runtime-") as temporary:
        root = Path(temporary)
        environment = dict(os.environ, TIPCL_HOME=str(root))
        names = set()
        for _ in range(2):
            subprocess.run([str(executable), "--self-test"], cwd=temporary, env=environment,
                           timeout=60, check=True, capture_output=True)
            report = json.loads((root / "self-test.json").read_text(encoding="utf-8"))
            actual_root = report.get("root")
            try:
                same_root = isinstance(actual_root, str) and os.path.samefile(actual_root, root)
            except OSError:
                same_root = False
            if not report.get("ok") or not same_root:
                raise ValueError(f"Isolated data root mismatch: expected {root}, got {actual_root!r}.")
            names.add(report["instance"])
        instances = list((root / ".minecraft").glob("*/.instance.json"))
        if len(instances) != 2 or len(names) != 2:
            raise ValueError("Instances did not persist across application restart.")
        if (root / "accounts.json").exists():
            raise ValueError("Smoke test unexpectedly accessed account storage.")
        print(json.dumps({"ok": True, "runtime": report["runtime"], "launches": 2,
                          "persistedInstances": len(instances), "executable": str(executable)}))


if __name__ == "__main__":
    try:
        smoke(Path(sys.argv[1]))
    except (OSError, ValueError, KeyError, subprocess.SubprocessError) as error:
        print(f"Published runtime smoke failed: {error}", file=sys.stderr)
        raise SystemExit(1)
