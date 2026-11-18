"""Check destructive-cleanup boundaries without contacting a Docker daemon."""

import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "tools" / "verify_relay_container.py"
SPEC = importlib.util.spec_from_file_location("verify_relay_container", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
relay = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(relay)


class RelayContainerVerifierTests(unittest.TestCase):
    def probe(self):
        return relay.RelayAcceptance(None, Path(tempfile.gettempdir()) / "unused-relay-report.json")

    def test_remote_daemon_is_rejected_before_any_command(self):
        probe = self.probe()
        with patch.dict(os.environ, {"DOCKER_HOST": "tcp://192.0.2.10:2375"}), patch.object(probe, "command") as command:
            with self.assertRaisesRegex(relay.AcceptanceFailure, "not local"):
                probe.local_engine(None)
        command.assert_not_called()

    def test_cleanup_refuses_a_resource_with_another_owner(self):
        probe = self.probe()
        probe.containers.append(probe.prefix + "-relay")
        inspected = subprocess.CompletedProcess([], 0, "another-run\n", "")
        with patch.object(relay.subprocess, "run", return_value=inspected) as calls:
            with self.assertRaisesRegex(relay.AcceptanceFailure, "ownership label"):
                probe.cleanup()
        self.assertEqual(1, calls.call_count)

    def test_cleanup_does_not_treat_an_unreachable_engine_as_success(self):
        probe = self.probe()
        probe.volumes.append(probe.prefix + "-state")
        inspected = subprocess.CompletedProcess([], 1, "", "Cannot connect to the Docker daemon")
        with patch.object(relay.subprocess, "run", return_value=inspected):
            with self.assertRaisesRegex(relay.AcceptanceFailure, "verify ownership"):
                probe.cleanup()
        self.assertEqual([], probe.report["cleanup"])

    def test_cleanup_accepts_an_already_removed_own_named_resource(self):
        probe = self.probe()
        name = probe.prefix + "-seed"
        probe.containers.append(name)
        inspected = subprocess.CompletedProcess([], 1, "", "Error: No such object: " + name)
        with patch.object(relay.subprocess, "run", return_value=inspected):
            probe.cleanup()
        self.assertTrue(probe.report["cleanup"][0]["alreadyAbsent"])

    def test_minimal_context_excludes_build_outputs_and_credentials(self):
        with tempfile.TemporaryDirectory() as workspace:
            root = Path(workspace)
            for project in ("HelL.Contracts", "HelL.Remote", "HelL.Relay"):
                source = root / "src" / project
                (source / "obj").mkdir(parents=True)
                (source / "bin").mkdir()
                (source / (project + ".csproj")).write_text("<Project />", encoding="utf-8")
                (source / "Source.cs").write_text("class Source {}", encoding="utf-8")
                (source / "obj" / "Generated.cs").write_text("excluded", encoding="utf-8")
                (source / "bin" / "secret.pfx").write_text("excluded", encoding="utf-8")
                (source / ".env").write_text("excluded", encoding="utf-8")
            dockerfile = root / "deploy" / "relay" / "Dockerfile"
            dockerfile.parent.mkdir(parents=True)
            dockerfile.write_text("FROM scratch", encoding="utf-8")
            probe = self.probe()
            try:
                with patch.object(relay, "ROOT", root), patch.object(probe, "command", return_value=""), patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}):
                    context, _, _ = probe.prepare()
                files = [str(path.relative_to(context)).replace("\\", "/") for path in context.rglob("*") if path.is_file()]
                self.assertEqual(7, len(files))
                self.assertFalse(any("/obj/" in path or "/bin/" in path or path.endswith(".env") for path in files))
                self.assertEqual(7, probe.report["buildContextFiles"])
            finally:
                probe.cleanup()

    def test_report_output_redacts_test_credentials(self):
        probe = self.probe()
        probe.secrets.append("private-test-credential")
        self.assertEqual("failure: [redacted]", probe.redact("failure: private-test-credential"))


if __name__ == "__main__":
    unittest.main()
