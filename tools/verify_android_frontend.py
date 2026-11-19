"""Exercise an isolated APK profile on a disposable Android emulator."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

PACKAGE = "io.hell.launcher.preview"
ACTIVITY = f"{PACKAGE}/{PACKAGE}.MainActivity"


def adb(*args: str, allow_failure: bool = False) -> str:
    completed = subprocess.run(["adb", *args], capture_output=True, text=True,
                               encoding="utf-8", errors="replace", timeout=90)
    if completed.returncode and not allow_failure:
        raise RuntimeError(f"adb {args[0]} failed: {completed.stderr.strip()}")
    return completed.stdout.strip()


def wait_for(test, description: str, seconds: int = 30):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = test()
        if value:
            return value
        time.sleep(0.5)
    raise TimeoutError(description)


def read_json(path: str):
    raw = adb("shell", "cat", path, allow_failure=True)
    try:
        return json.loads(raw)
    except (ValueError, TypeError):
        return None


def tap_match(labels: tuple[str, ...], *, editable: bool = False, scroll: bool = False) -> None:
    for attempt in range(5 if scroll else 1):
        adb("shell", "uiautomator", "dump", "/sdcard/hell-frontend-window.xml")
        document = ET.fromstring(adb("shell", "cat", "/sdcard/hell-frontend-window.xml"))
        matches = []
        for node in document.iter("node"):
            name = node.get("text", "") + " " + node.get("content-desc", "")
            if not (any(label in name for label in labels)
                    or editable and "EditText" in node.get("class", "")):
                continue
            bounds = [int(part) for part in re.findall(r"\d+", node.get("bounds", ""))]
            if len(bounds) != 4 or bounds[2] <= bounds[0] or bounds[3] <= bounds[1]:
                continue
            matches.append((0 if "EditText" in node.get("class", "") else 1,
                            (bounds[2] - bounds[0]) * (bounds[3] - bounds[1]), bounds))
        if matches:
            _, _, bounds = min(matches)
            adb("shell", "input", "tap", str((bounds[0] + bounds[2]) // 2), str((bounds[1] + bounds[3]) // 2))
            return
        if scroll and attempt < 4:
            size = re.search(r"(\d+)x(\d+)", adb("shell", "wm", "size"))
            if size is None:
                break
            width, height = map(int, size.groups())
            adb("shell", "input", "swipe", str(width // 2), str(int(height * 0.78)),
                str(width // 2), str(int(height * 0.34)), "300")
            time.sleep(0.4)
    raise RuntimeError("The emulator accessibility tree did not expose the required control: " + ", ".join(labels))


def screenshot(path: Path) -> None:
    with path.open("wb") as output:
        subprocess.run(["adb", "exec-out", "screencap", "-p"], stdout=output, check=True, timeout=30)


def verify(apk: Path, output: Path) -> None:
    if adb("shell", "getprop", "ro.kernel.qemu") != "1":
        raise RuntimeError("This check only accepts a disposable emulator; physical devices are excluded.")
    if not apk.is_file() or output.exists():
        raise ValueError("Use an existing APK and a new evidence directory.")
    output.mkdir(parents=True)
    session = uuid.uuid4().hex
    evidence = f"/sdcard/Android/data/{PACKAGE}/files/ui-evidence/{session}"
    original_scale = adb("shell", "settings", "get", "system", "font_scale")
    try:
        adb("install", "-r", str(apk))
        adb("shell", "am", "force-stop", PACKAGE)
        adb("shell", "settings", "put", "system", "font_scale", "1.0")
        adb("shell", "am", "start", "-W", "-n", ACTIVITY, "--ez", "hell_ui_smoke", "true",
            "--es", "hell_smoke_session", session)

        def smoke_finished():
            failure = adb("shell", "cat", evidence + "/mobile-smoke-error.txt", allow_failure=True)
            if failure and not failure.startswith("cat:"):
                raise RuntimeError("Android frontend smoke failed: " + failure)
            return read_json(evidence + "/mobile-smoke.json")

        report = wait_for(smoke_finished, "Android UI smoke did not finish.", seconds=240)
        if not report.get("ok"):
            raise RuntimeError("Android UI smoke returned an unsuccessful receipt.")
        remediation = read_json(evidence + "/mobile-frontend-remediation.json")
        if not remediation or not remediation.get("ok"):
            raise RuntimeError("Android frontend remediation assertions did not pass.")
        screenshot(output / "01-home.png")
        tap_match(("管理当前实例", "管理实例"))
        time.sleep(0.5)
        screenshot(output / "01b-manage.png")
        field = wait_for(lambda: (value.get("instanceNameTap") if (value := read_json(evidence + "/mobile-platform-events.json"))
                                  and value.get("page") == "manage" else None),
                         "The visible instance-name field did not publish its measured native bounds.")
        adb("shell", "input", "tap", str(field["x"]), str(field["y"]))
        keyboard = wait_for(lambda: (value if (value := read_json(evidence + "/mobile-platform-events.json"))
                                    and value.get("keyboardOpen") else None), "The real Android keyboard-open event was not observed.")
        screenshot(output / "02-keyboard.png")
        adb("shell", "input", "keyevent", "4")
        closed = wait_for(lambda: (value if (value := read_json(evidence + "/mobile-platform-events.json"))
                                  and not value.get("keyboardOpen") else None), "The Android keyboard did not close.")
        if closed.get("page") != "manage":
            raise RuntimeError("The first system back navigated away instead of only closing the keyboard.")
        before_back = int(closed.get("backEvents", 0))
        adb("shell", "input", "keyevent", "4")
        returned = wait_for(lambda: (value if (value := read_json(evidence + "/mobile-platform-events.json"))
                                    and value.get("backEvents", 0) > before_back and value.get("page") != "manage" else None),
                            "The application did not handle Android back after the keyboard closed.")
        screenshot(output / "03-back.png")
        if not all(returned.get(key) for key in ("backHook", "insetsHook", "inputPaneHook")):
            raise RuntimeError("An Android platform adaptation hook is missing.")
        if any(value < 0 for value in returned["safeArea"].values()):
            raise RuntimeError("Android safe-area padding is invalid.")
        adb("shell", "settings", "put", "system", "font_scale", "1.5")
        adb("shell", "am", "force-stop", PACKAGE)
        adb("shell", "am", "start", "-W", "-n", ACTIVITY, "--ez", "hell_verify_restart", "true",
            "--es", "hell_smoke_session", session)
        enlarged = wait_for(lambda: (value if (value := read_json(evidence + "/mobile-platform-events.json"))
                                     and value.get("systemTextScale", 0) >= 1.49 else None),
                            "The system font setting was not applied after Activity recreation.")
        screenshot(output / "04-system-font-150.png")
        adb("pull", evidence, str(output / "ui"))
        acceptance = {"ok": True, "scope": "Disposable Android emulator; not a physical device or TalkBack certification.",
                      "session": session, "abi": adb("shell", "getprop", "ro.product.cpu.abi"),
                      "keyboardOpenClose": keyboard["keyboardEvents"] < closed["keyboardEvents"],
                      "keyboardBackOrdering": True, "applicationBack": True, "measuredInputTarget": field, "platform": returned,
                      "fontScaleObserved": enlarged["systemTextScale"], "physicalDeviceVerified": False, "talkBackVerified": False}
        (output / "acceptance.json").write_text(json.dumps(acceptance, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(acceptance))
    finally:
        screenshot(output / "last-native-screen.png")
        adb("pull", "/sdcard/hell-frontend-window.xml", str(output / "last-native-tree.xml"), allow_failure=True)
        adb("pull", evidence, str(output / "ui-failure-or-final"), allow_failure=True)
        adb("shell", "settings", "put", "system", "font_scale", original_scale if original_scale != "null" else "1.0", allow_failure=True)
        adb("shell", "am", "force-stop", PACKAGE, allow_failure=True)
        (output / "logcat.txt").write_text(adb("logcat", "-d", "-t", "1500", allow_failure=True), encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("apk", type=Path)
    parser.add_argument("output", type=Path)
    arguments = parser.parse_args()
    verify(arguments.apk, arguments.output)
