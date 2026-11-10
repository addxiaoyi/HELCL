"""Verify all portable artifacts in a directory against their final checksum manifests."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys


def verify(folder: Path) -> None:
    manifests = list(folder.glob("HelL-*.json"))
    if not manifests:
        raise ValueError("No portable manifests found.")
    sums = {}
    for line in (folder / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
        checksum, name = line.split("  ", 1)
        if Path(name).name != name or name in sums or "/" in name or "\\" in name:
            raise ValueError("Invalid or duplicate checksum filename.")
        with (folder / name).open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        if checksum != actual:
            raise ValueError(f"SHA-256 mismatch: {name}")
        sums[name] = checksum
    for path in manifests:
        manifest = json.loads(path.read_text(encoding="utf-8"))
        name = manifest["file"]
        if name not in sums or path.name not in sums or manifest["sha256"] != sums[name]:
            raise ValueError(f"Manifest does not match SHA256SUMS: {path.name}")
        if manifest["size"] != (folder / name).stat().st_size:
            raise ValueError(f"Incorrect archive size: {name}")
        print(f"Verified {name}: {manifest['signature']}, {manifest['sha256']}")


if __name__ == "__main__":
    try:
        verify(Path(sys.argv[1]))
    except (OSError, ValueError, KeyError, IndexError) as error:
        print(f"Verification failed: {error}", file=sys.stderr)
        raise SystemExit(1)
