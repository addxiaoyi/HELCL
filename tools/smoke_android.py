"""Install the signed APK and inspect UI smoke evidence from an isolated emulator."""
from __future__ import annotations
import json
from pathlib import Path
import subprocess
import sys
import time


def adb(*args: str, allowed: tuple[int, ...] = (0,)) -> str:
    call = subprocess.run(["adb", *args], capture_output=True, text=True, timeout=90)
    if call.returncode not in allowed:
        raise RuntimeError(f"adb {args[0]} failed: {call.stdout} {call.stderr}")
    return call.stdout.strip()


def smoke(apk: Path, output: Path) -> None:
    if not apk.is_file() or output.exists():
        raise ValueError("Use an existing APK and a new evidence directory.")
    output.mkdir(parents=True)
    package = "io.hell.launcher.preview"
    evidence = f"/sdcard/Android/data/{package}/files/ui-evidence"
    adb("install", "-r", str(apk))
    adb("shell", "am", "start", "-W", "-n", f"{package}/{package}.MainActivity", "--ez", "hell_ui_smoke", "true", "--ez", "hell_catalog_live", "true")
    deadline = time.monotonic() + 300
    try:
        while time.monotonic() < deadline:
            files = adb("shell", "ls", evidence, allowed=(0, 1))
            if "mobile-smoke.json" in files or "mobile-smoke-error.txt" in files:
                break
            time.sleep(2)
        adb("pull", evidence, str(output / "ui"))
        report = json.loads((output / "ui/mobile-smoke.json").read_text(encoding="utf-8"))
        if not report.get("ok") or not all(report.get(k) for k in ("created", "renamed", "grouped", "pinned", "resources")):
            raise ValueError("Android native UI checks did not pass.")
        if not report.get("nativeResourceDownload"):
            raise ValueError("Android native catalog download did not complete.")
        catalog = json.loads((output / "ui/mobile-catalog-live.json").read_text(encoding="utf-8"))
        if not catalog.get("ok") or not catalog.get("iconRendered") or catalog.get("bytes", 0) <= 0:
            raise ValueError("Android live catalog report is incomplete.")
        for page in ("library", "manage", "resources-empty", "resources", "detail", "download", "about"):
            if not (output / f"ui/mobile-{page}.png").is_file():
                raise ValueError(f"Missing native mobile screenshot: {page}")
        with (output / "android-screen.png").open("wb") as screenshot:
            subprocess.run(["adb", "exec-out", "screencap", "-p"], stdout=screenshot, check=True, timeout=20)
        adb("shell", "am", "force-stop", package)
        adb("shell", "am", "start", "-W", "-n", f"{package}/{package}.MainActivity", "--ez", "hell_verify_restart", "true")
        restart_deadline = time.monotonic() + 30
        while time.monotonic() < restart_deadline:
            if "mobile-restart.json" in adb("shell", "ls", evidence, allowed=(0, 1)):
                break
            time.sleep(1)
        adb("pull", evidence + "/mobile-restart.json", str(output / "mobile-restart.json"))
        restart = json.loads((output / "mobile-restart.json").read_text(encoding="utf-8"))
        if not restart.get("ok") or restart.get("restoredInstances") != 1:
            raise RuntimeError("Android instance persistence check failed.")
        pid = adb("shell", "pidof", package)
        if not pid: raise RuntimeError("APK did not remain alive after restart.")
        report["nativeApkRestartAlive"] = True
        report["nativeApkRestoredInstances"] = restart["restoredInstances"]
        report["deviceAbi"] = adb("shell", "getprop", "ro.product.cpu.abi")
        report["androidVersion"] = adb("shell", "getprop", "ro.build.version.release")
        (output / "acceptance.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report))
    finally:
        (output / "logcat.txt").write_text(adb("logcat", "-d", "-t", "1500"), encoding="utf-8")


if __name__ == "__main__":
    try:
        smoke(Path(sys.argv[1]), Path(sys.argv[2]))
    except (OSError, ValueError, KeyError, IndexError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"Android APK smoke failed: {error}", file=sys.stderr)
        raise SystemExit(1)
