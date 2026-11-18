"""Copy an explicit source snapshot into the isolated GitHub test checkout."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

TREES = (
    "src/HelL.Contracts", "src/HelL.Remote", "src/HelL.Relay", "src/HelL.Companion",
    "src/HelL.Core", "src/HelL/Core", "src/HelL.Mobile", "src/HelL.Design",
    "src/HelL/Resources/locales", "src/HelL/Resources/Brand", "src/HelL/Resources/ai",
    "src/HelL/Resources/news", "src/HelL/Resources/catalog",
    "tests/HelL.Remote.Tests", "tests/HelL.Portable.Tests", "tests/HelL.Mobile.Tests",
    "tools/HelL.Credentials.Verify", "tools/HelL.Relay.Acceptance",
    "tools/HelL.Companion.Host", "tools/HelL.Mobile.Preview", "deploy/relay",
    "docs/licenses",
)
FILES = (
    "deploy/relay/Dockerfile",
    "src/HelL/Models/CatalogItem.cs", "src/HelL.Android/CredentialEnvelope.cs",
    "src/HelL.Desktop/Models/DesktopTask.cs", "src/HelL.Desktop/Controls/ProjectPicture.cs",
    "src/HelL.Desktop/Services/DesktopImages.cs", "src/HelL.Desktop/Services/ImageBounds.cs",
    "tools/HelL.MinecraftMcp/package.json", "tools/HelL.MinecraftMcp/package-lock.json",
    "tools/run_native_credentials.sh", "tools/verify_relay_container.py",
    "tests/test_verify_relay_container.py", ".github/workflows/native-credentials.yml",
    "tools/prepare_remote_ci.py", "tests/test_prepare_remote_ci.py",
    ".github/workflows/relay-acceptance.yml", ".github/workflows/remote-mobile-acceptance.yml",
    "docs/native-credentials.md", "docs/remote-companion.md", "docs/backend-hardening-2026-11-18.md",
    "docs/remote-followup-2026-11-18.md",
    "LICENSE", "THIRD_PARTY_NOTICES.md",
)
SUFFIXES = {".cs", ".csproj", ".axaml", ".xaml", ".json", ".png", ".jpg", ".jpeg", ".svg", ".otf", ".ttf", ".md", ".txt", ".js", ".yml", ".yaml", ".sh", ".py"}
SKIP = {"bin", "obj", "node_modules", "artifacts", "TestResults", ".git", ".vs"}
TOKEN = re.compile(r"(?:github_pat_[A-Za-z0-9_]{30,}|gh[pousr]_[A-Za-z0-9_]{25,}|sk-[A-Za-z0-9_-]{30,}|AKIA[A-Z0-9]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)")


def git(stage: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(stage), *args], check=True, text=True, capture_output=True).stdout.strip()


def snapshot(source: Path, stage: Path, manifest: Path) -> None:
    source, stage = source.resolve(), stage.resolve()
    if source == stage or not (stage / ".git").is_dir() or stage.parent != source / ".tmp":
        raise ValueError("Use the isolated checkout directly under this workspace's .tmp directory.")
    if git(stage, "remote", "get-url", "origin") not in {
        "https://github.com/addxiaoyi/HELCL.git", "https://github.com/addxiaoyi/HELCL",
    }:
        raise ValueError("The staging checkout is not the verified HELCL repository.")
    selected: set[Path] = set()
    for tree in TREES:
        directory = source / tree
        if not directory.is_dir():
            raise FileNotFoundError(tree)
        for path in directory.rglob("*"):
            relative = path.relative_to(source)
            if path.is_file() and path.suffix.lower() in SUFFIXES and not SKIP.intersection(relative.parts):
                selected.add(relative)
    for path in (source / "tests/HelL.Tests").glob("*.cs"):
        selected.add(path.relative_to(source))
    for path in (source / "tools/HelL.MinecraftMcp/dist").glob("*"):
        if path.is_file() and path.suffix.lower() in {".js", ".json"}:
            selected.add(path.relative_to(source))
    selected.update(Path(path) for path in FILES)
    entries = []
    for relative in sorted(selected):
        path = source / relative
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(source):
            raise ValueError(f"Missing, linked or unsafe source path: {relative}")
        content = path.read_bytes()
        if path.suffix.lower() not in {".png", ".jpg", ".jpeg", ".otf", ".ttf"}:
            text = content.decode("utf-8-sig")
            for match in TOKEN.finditer(text):
                if any(word in match.group().lower() for word in ("fictional", "example", "placeholder", "test")):
                    continue
                line = text.count("\n", 0, match.start()) + 1
                raise ValueError(f"Potential secret in {relative}:{line}; no snapshot was authorized for that file.")
        entries.append({"path": relative.as_posix(), "sha256": hashlib.sha256(content).hexdigest(), "bytes": len(content)})
    for entry in entries:
        target = stage / entry["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / entry["path"], target)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps({"repository": "addxiaoyi/HELCL", "baseCommit": git(stage, "rev-parse", "HEAD"), "files": entries}, indent=2), encoding="utf-8")
    print(json.dumps({"files": len(entries), "bytes": sum(row["bytes"] for row in entries), "manifest": str(manifest)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    snapshot(args.source, args.stage, args.manifest)
