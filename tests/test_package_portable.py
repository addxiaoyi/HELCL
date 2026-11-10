"""Release regressions use dummy publish trees; no binaries are executed or signed."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("package_portable", Path(__file__).resolve().parents[1] / "tools/package_portable.py")
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)
verify_spec = importlib.util.spec_from_file_location("verify_portable", Path(__file__).resolve().parents[1] / "tools/verify_portable.py")
verification = importlib.util.module_from_spec(verify_spec)
verify_spec.loader.exec_module(verification)


class PortablePackageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.source = self.root / "publish"
        self.source.mkdir()
        self.output = self.root / "packages"
        self.version = "3.2.0-preview.1"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def fixture(self, runtime: str = "win-x64") -> None:
        names = ["HelL.Desktop.dll", "HelL.Core.dll", "HelL.Desktop.runtimeconfig.json"]
        names += ["HelL.Desktop.exe", "coreclr.dll"] if runtime.startswith("win-") else ["HelL.Desktop", "libcoreclr.so"]
        for name in names:
            (self.source / name).write_bytes(b"test fixture, not executable")
        (self.source / "portable-build.json").write_text(json.dumps({"schema": 1, "version": self.version, "runtime": runtime, "selfContained": True}))
        target = f".NETCoreApp,Version=v10.0/{runtime}"
        (self.source / "HelL.Desktop.deps.json").write_text(json.dumps({"runtimeTarget": {"name": target}, "targets": {target: {f"HelL.Desktop/{self.version}": {}}}}))
        (self.source / "HelL.Desktop.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.10"}]}}))

    def test_windows_archive_and_manifest_match_final_bytes(self) -> None:
        self.fixture()
        archive = packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")
        manifest = json.loads(next(self.output.glob("*.json")).read_text())
        self.assertEqual(manifest["sha256"], packaging.sha256(archive))
        self.assertEqual(manifest["size"], archive.stat().st_size)
        self.assertEqual(manifest["signature"], "unsigned")
        self.assertNotIn("url", manifest)
        with zipfile.ZipFile(archive) as bundle:
            self.assertTrue(any(name.endswith("/data/") for name in bundle.namelist()))
            self.assertTrue(any(name.endswith("/READ-ME.md") for name in bundle.namelist()))
        for line in (self.output / "SHA256SUMS").read_text().splitlines():
            checksum, name = line.split("  ")
            self.assertEqual(checksum, packaging.sha256(self.output / name))

    def test_version_and_runtime_mismatches_leave_no_package(self) -> None:
        self.fixture()
        for runtime, version in [("win-x64", "9.9.9"), ("linux-arm64", self.version)]:
            with self.assertRaises(ValueError):
                packaging.package(self.source, self.output, runtime, version, "unsigned")
        self.assertFalse(self.output.exists())

    def test_local_data_never_enters_release(self) -> None:
        self.fixture()
        (self.source / "accounts.json").write_text('{}')
        with self.assertRaisesRegex(ValueError, "user state"):
            packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")
        self.assertFalse(self.output.exists())

    def test_framework_dependent_build_is_rejected(self) -> None:
        self.fixture()
        (self.source / "HelL.Desktop.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.10"}}}))
        with self.assertRaisesRegex(ValueError, "external .NET"):
            packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")

    def test_encrypted_accounts_are_private_and_must_not_ship(self) -> None:
        self.fixture()
        (self.source / "portable-account.json").write_text('{"Ciphertext":"private"}')
        with self.assertRaisesRegex(ValueError, "user state"):
            packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")

    def test_modified_archive_fails_verification(self) -> None:
        self.fixture()
        archive = packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")
        verification.verify(self.output)
        with archive.open("ab") as stream:
            stream.write(b"modified after signing")
        with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
            verification.verify(self.output)

    def test_existing_output_and_nested_output_are_rejected(self) -> None:
        self.fixture()
        self.output.mkdir()
        with self.assertRaises(FileExistsError):
            packaging.package(self.source, self.output, "win-x64", self.version, "unsigned")
        with self.assertRaises(ValueError):
            packaging.package(self.source, self.source / "packages", "win-x64", self.version, "unsigned")

    def test_invalid_signature_modes_fail_closed(self) -> None:
        self.fixture("linux-x64")
        for signature in ("adhoc", "authenticode"):
            with self.assertRaises(ValueError):
                packaging.package(self.source, self.output, "linux-x64", self.version, signature)
        if sys.platform != "darwin":
            with self.assertRaisesRegex(ValueError, "on macOS"):
                packaging.package(self.source, self.output, "osx-x64", self.version, "adhoc")

    def test_linux_archive_keeps_executable_mode(self) -> None:
        self.fixture("linux-x64")
        (self.source / "createdump").write_bytes(b"runtime crash helper fixture")
        archive = packaging.package(self.source, self.output, "linux-x64", self.version, "unsigned")
        with tarfile.open(archive) as bundle:
            executables = [member for member in bundle if Path(member.name).name in {"HelL.Desktop", "createdump"}]
            self.assertEqual(2, len(executables))
            self.assertTrue(all(member.mode & 0o111 for member in executables))


if __name__ == "__main__":
    unittest.main()
