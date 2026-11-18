from pathlib import Path
import importlib.util
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("prepare_remote_ci", Path(__file__).resolve().parents[1] / "tools/prepare_remote_ci.py")
assert SPEC and SPEC.loader
ci = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ci)


class SourceSnapshotTests(unittest.TestCase):
    def test_extensionless_dockerfile_is_explicitly_required(self):
        self.assertIn("deploy/relay/Dockerfile", ci.FILES)

    def test_missing_required_source_is_rejected_before_copying(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stage = root / ".tmp" / "ci"
            (stage / ".git").mkdir(parents=True)
            with patch.object(ci, "TREES", ()), patch.object(ci, "FILES", ("deploy/relay/Dockerfile",)), patch.object(ci, "git", return_value="https://github.com/addxiaoyi/HELCL.git"):
                with self.assertRaises(ValueError):
                    ci.snapshot(root, stage, root / "report.json")
            self.assertFalse((stage / "deploy").exists())

    def test_a_different_origin_is_rejected_before_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stage = root / ".tmp" / "ci"
            (stage / ".git").mkdir(parents=True)
            with patch.object(ci, "git", return_value="https://github.com/example/other.git"):
                with self.assertRaises(ValueError):
                    ci.snapshot(root, stage, root / "report.json")
            self.assertFalse((root / "report.json").exists())


if __name__ == "__main__":
    unittest.main()
