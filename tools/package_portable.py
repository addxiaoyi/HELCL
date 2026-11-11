"""Package self-contained publish output; signatures must precede checksums."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile

RUNTIMES = ("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
ROOT = Path(__file__).resolve().parent.parent
VERSION = re.compile(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?")
def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_publish(source: Path, runtime: str, version: str) -> None:
    meta = json.loads((source / "portable-build.json").read_text(encoding="utf-8-sig"))
    if meta != {"schema": 1, "version": version, "runtime": runtime, "selfContained": True}:
        raise ValueError("Publish metadata does not match requested version/runtime or is not self-contained.")
    deps = json.loads((source / "HelL.Desktop.deps.json").read_text(encoding="utf-8-sig"))
    target = deps["runtimeTarget"]["name"]
    if not target.endswith("/" + runtime) or f"HelL.Desktop/{version}" not in deps["targets"][target]:
        raise ValueError("Dependency manifest version/runtime does not match publish metadata.")
    config = json.loads((source / "HelL.Desktop.runtimeconfig.json").read_text(encoding="utf-8-sig"))["runtimeOptions"]
    if not config.get("includedFrameworks") or config.get("framework") or config.get("frameworks"):
        raise ValueError("Runtime configuration requires an external .NET installation.")
    executable = "HelL.Desktop.exe" if runtime.startswith("win-") else "HelL.Desktop"
    runtime_library = "coreclr.dll" if runtime.startswith("win-") else "libcoreclr.dylib" if runtime.startswith("osx-") else "libcoreclr.so"
    for name in (executable, runtime_library, "HelL.Desktop.dll", "HelL.Core.dll", "HelL.Desktop.runtimeconfig.json"):
        if not (source / name).is_file():
            raise ValueError(f"Incomplete publish output: missing {name}")
    for entry in source.rglob("*"):
        if entry.is_symlink():
            raise ValueError(f"Publish output contains an unexpected symlink: {entry.name}")
        if entry.name.lower() in {"data", "config.json", "accounts.json", ".minecraft", "logs", ".launcher.lock"} or entry.name.lower().startswith("portable-account."):
            raise ValueError(f"Refusing to package local user state: {entry.name}")


def windows_signature(folder: Path, subject: str) -> None:
    if sys.platform != "win32" or not subject:
        raise ValueError("Authenticode packaging requires Windows and --expected-subject.")
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "tools/verify_windows_signature.ps1"),
                    "-Path", str(folder), "-ExpectedSubject", subject], check=True)


def mac_bundle(folder: Path, version: str) -> Path:
    app = folder / "HelL.app"
    contents = app / "Contents"
    binaries = contents / "MacOS"
    binaries.mkdir(parents=True)
    for entry in tuple(folder.iterdir()):
        if entry != app:
            shutil.move(str(entry), binaries / entry.name)
    resources = contents / "Resources"
    resources.mkdir()
    info = {"CFBundleName": "HelL", "CFBundleDisplayName": "HelL Preview", "CFBundleIdentifier": "io.hell.launcher.preview",
            "CFBundleExecutable": "HelL.Desktop", "CFBundlePackageType": "APPL", "CFBundleInfoDictionaryVersion": "6.0",
            "CFBundleVersion": version.split("-")[0], "CFBundleShortVersionString": version.split("-")[0],
            "LSMinimumSystemVersion": "14.0", "NSHighResolutionCapable": True, "CFBundleIconFile": "HelL.icns"}
    with (contents / "Info.plist").open("wb") as stream:
        plistlib.dump(info, stream)
    with tempfile.TemporaryDirectory(prefix="hell-icon-") as temporary:
        iconset = Path(temporary) / "HelL.iconset"
        iconset.mkdir()
        for size in (16, 32, 128, 256, 512):
            for scale in (1, 2):
                name = f"icon_{size}x{size}{'@2x' if scale == 2 else ''}.png"
                subprocess.run(["sips", "-z", str(size * scale), str(size * scale),
                                str(ROOT / "src/HelL/Resources/Brand/Helstera.png"), "--out", str(iconset / name)],
                               check=True, stdout=subprocess.DEVNULL)
        subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(resources / "HelL.icns")], check=True)
    (binaries / "HelL.Desktop").chmod(0o755)
    # Bundle signing must include the managed assemblies beside the .NET app host.
    subprocess.run(["codesign", "--force", "--deep", "--sign", "-", "--timestamp=none", str(app)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app)], check=True)
    subprocess.run([sys.executable, str(ROOT / "tools/smoke_portable.py"), str(binaries / "HelL.Desktop")], check=True)
    return app


def package(source: Path, output: Path, runtime: str, version: str, signature: str, subject: str = "", repository: str = "") -> Path:
    source, output = source.resolve(strict=True), output.resolve()
    if not VERSION.fullmatch(version) or runtime not in RUNTIMES or signature not in {"unsigned", "authenticode", "adhoc"}:
        raise ValueError("Invalid release version or runtime.")
    if repository and not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid release repository.")
    if output == source or source in output.parents:
        raise ValueError("Package output must be outside publish output.")
    if output.exists():
        raise FileExistsError(f"Package output already exists: {output}")
    if runtime.startswith("osx-") and (sys.platform != "darwin" or signature != "adhoc"):
        raise ValueError("macOS packages must be bundled, ad-hoc signed and verified on macOS.")
    if signature == "adhoc" and not runtime.startswith("osx-"):
        raise ValueError("Ad-hoc signing is only supported for macOS.")
    if signature == "authenticode" and not runtime.startswith("win-"):
        raise ValueError("Authenticode is only supported for Windows.")
    check_publish(source, runtime, version)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="hell-package-", dir=output.parent) as temporary:
        stage = Path(temporary)
        name = f"HelL-{version}-{runtime}"
        folder = stage / name
        shutil.copytree(source, folder, ignore=shutil.ignore_patterns("*.pdb"))
        if signature == "authenticode":
            windows_signature(folder, subject)
        elif signature == "adhoc":
            mac_bundle(folder, version)
        elif not runtime.startswith("win-"):
            (folder / "HelL.Desktop").chmod(0o755)
        (folder / "data").mkdir()
        shutil.copyfile(ROOT / "docs/downloads.md", folder / "READ-ME.md")
        shutil.copyfile(ROOT / "LICENSE", folder / "LICENSE")
        shutil.copyfile(ROOT / "THIRD_PARTY_NOTICES.md", folder / "THIRD_PARTY_NOTICES.md")
        shutil.copytree(ROOT / "docs/licenses", folder / "licenses")
        artifact = stage / "artifacts"
        artifact.mkdir()
        if runtime.startswith("linux-"):
            archive = artifact / f"{name}.tar.gz"
            with tarfile.open(archive, "w:gz", format=tarfile.PAX_FORMAT) as bundle:
                bundle.add(folder, arcname=name, filter=linux_permissions)
        elif runtime.startswith("osx-"):
            archive = artifact / f"{name}.zip"
            subprocess.run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(folder), str(archive)], check=True)
        else:
            archive = artifact / f"{name}.zip"
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
                for entry in sorted(folder.rglob("*")):
                    bundle.write(entry, entry.relative_to(stage))
        digest = sha256(archive)
        manifest = {"schema": 1, "channel": "preview", "version": version, "runtime": runtime,
                    "file": archive.name, "sha256": digest, "size": archive.stat().st_size,
                    "signature": signature, "notarized": False, "automaticUpdate": False}
        if repository:
            manifest["url"] = f"https://github.com/{repository}/releases/download/v{version}/{archive.name}"
        manifest_path = artifact / f"{name}.json"
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        (artifact / "SHA256SUMS").write_text(f"{digest}  {archive.name}\n{sha256(manifest_path)}  {manifest_path.name}\n", encoding="utf-8")
        artifact.rename(output)
    return output / archive.name


def linux_permissions(member: tarfile.TarInfo) -> tarfile.TarInfo:
    # Windows chmod cannot set Unix execute bits, so write portable permissions to the archive itself.
    member.uid = member.gid = 0
    member.uname = member.gname = ""
    executable = Path(member.name).name in {"HelL.Desktop", "createdump"}
    member.mode = 0o755 if member.isdir() or executable or member.mode & 0o111 else 0o644
    return member


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--runtime", required=True, choices=RUNTIMES)
    parser.add_argument("--version", required=True)
    parser.add_argument("--signature", choices=("unsigned", "authenticode", "adhoc"), default="unsigned")
    parser.add_argument("--expected-subject", default="")
    parser.add_argument("--repository", default="")
    args = parser.parse_args()
    try:
        print(package(args.source, args.output, args.runtime, args.version, args.signature, args.expected_subject, args.repository))
        return 0
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"Packaging failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
