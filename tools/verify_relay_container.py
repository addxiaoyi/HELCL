#!/usr/bin/env python3
"""Exercise the relay behind a private, CA-validated Nginx HTTPS/WSS proxy."""

from __future__ import annotations

import argparse
import base64
import ipaddress
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import uuid


LABEL = "hell.relay.acceptance"
SDK_IMAGE = "mcr.microsoft.com/dotnet/sdk:10.0"
RUNTIME_IMAGE = "mcr.microsoft.com/dotnet/aspnet:10.0"
PROXY_IMAGE = "nginx:1.28-alpine"
ROOT = Path(__file__).resolve().parents[1]


class AcceptanceFailure(RuntimeError):
    """A failed check whose message contains no test credentials."""


class RelayAcceptance:
    def __init__(self, context: str | None, report: Path) -> None:
        self.run_id = uuid.uuid4().hex[:12]
        self.prefix = f"hell-relay-acceptance-{self.run_id}"
        self.docker = ["docker"] + (["--context", context] if context else [])
        self.report_path = report
        self.secrets: list[str] = []
        self.temporary: Path | None = None
        self.containers: list[str] = []
        self.networks: list[str] = []
        self.volumes: list[str] = []
        self.images: list[str] = []
        self.report: dict[str, object] = {
            "runId": self.run_id,
            "scope": "One runner, isolated container network; not two physical devices or a public deployment.",
            "checks": [],
            "imageDigests": {},
            "cleanup": [],
            "status": "running",
        }

    def redact(self, output: str) -> str:
        for credential in self.secrets:
            output = output.replace(credential, "[redacted]")
        return output

    def command(self, args: list[str], *, timeout: int = 120, show: bool = False) -> str:
        completed = subprocess.run(args, capture_output=True, text=True, timeout=timeout, check=False)
        output = self.redact(completed.stdout + completed.stderr)
        if show and output:
            print(output, end="" if output.endswith("\n") else "\n", flush=True)
        if completed.returncode:
            raise AcceptanceFailure(f"{Path(args[0]).name} command failed with exit {completed.returncode}:\n{output[-8000:]}")
        return completed.stdout.strip()

    def check(self, name: str) -> None:
        checks = self.report["checks"]
        assert isinstance(checks, list)
        checks.append(name)
        print("PASS " + name, flush=True)

    def local_engine(self, context: str | None) -> None:
        endpoint = os.environ.get("DOCKER_HOST") if context is None else None
        if not endpoint:
            selected = context or self.command(["docker", "context", "show"])
            endpoint = self.command(["docker", "context", "inspect", selected, "--format", "{{.Endpoints.docker.Host}}"])
        local = endpoint.startswith(("unix://", "npipe://", "tcp://127.0.0.1:", "tcp://localhost:", "tcp://[::1]:"))
        if not local:
            raise AcceptanceFailure("The selected Docker endpoint is not local; refusing to contact another machine.")
        version = self.command(self.docker + ["version", "--format", "{{.Server.Version}}"])
        self.report["dockerVersion"] = version
        self.check("local Docker engine reachable")

    def prepare(self) -> tuple[Path, Path, Path]:
        self.temporary = Path(tempfile.mkdtemp(prefix="hell-relay-acceptance-")).resolve()
        context = self.temporary / "context"
        copied = 0
        for project in ("HelL.Contracts", "HelL.Remote", "HelL.Relay"):
            source = ROOT / "src" / project
            for original in source.rglob("*"):
                relative = original.relative_to(source)
                if not original.is_file() or any(part.lower() in {"bin", "obj"} for part in relative.parts):
                    continue
                if original.suffix.lower() not in {".cs", ".csproj", ".props", ".targets"}:
                    continue
                target = context / "src" / project / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(original, target)
                copied += 1
        dockerfile = context / "deploy" / "relay" / "Dockerfile"
        dockerfile.parent.mkdir(parents=True)
        shutil.copy2(ROOT / "deploy" / "relay" / "Dockerfile", dockerfile)
        self.report["buildContextFiles"] = copied + 1
        self.check("minimal build context excludes bin, obj, credentials and the rest of the workspace")
        app = self.temporary / "app"
        self.command([
            "dotnet", "publish", str(ROOT / "tools" / "HelL.Relay.Acceptance" / "HelL.Relay.Acceptance.csproj"),
            "-c", "Release", "--artifacts-path", str(self.temporary / "dotnet-artifacts"),
            "-o", str(app), "--nologo", "-v", "minimal",
        ], timeout=300, show=True)
        certificates = self.temporary / "certificates"
        certificates.mkdir()
        self.command([
            "openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
            "-subj", "/CN=HelL isolated acceptance CA", "-addext", "basicConstraints=critical,CA:TRUE",
            "-keyout", str(certificates / "ca.key"), "-out", str(certificates / "ca.crt"),
        ])
        self.command([
            "openssl", "req", "-newkey", "rsa:2048", "-nodes", "-subj", "/CN=relay-proxy",
            "-keyout", str(certificates / "server.key"), "-out", str(certificates / "server.csr"),
        ])
        extensions = certificates / "server.ext"
        extensions.write_text("subjectAltName=DNS:relay-proxy\nextendedKeyUsage=serverAuth\n", encoding="utf-8")
        self.command([
            "openssl", "x509", "-req", "-in", str(certificates / "server.csr"),
            "-CA", str(certificates / "ca.crt"), "-CAkey", str(certificates / "ca.key"),
            "-CAcreateserial", "-days", "1", "-extfile", str(extensions),
            "-out", str(certificates / "server.crt"),
        ])
        for private_key in certificates.glob("*.key"):
            private_key.chmod(0o600)
        operator = base64.b64encode(secrets.token_bytes(32)).decode()
        signing = base64.b64encode(secrets.token_bytes(32)).decode()
        self.secrets.extend((operator, signing))
        if os.environ.get("GITHUB_ACTIONS") == "true":
            for credential in self.secrets:
                print("::add-mask::" + credential, flush=True)
        environment = self.temporary / "private.env"
        environment.write_text(
            f"Relay__OperatorToken={operator}\nRelay__SigningKey={signing}\n"
            f"HELL_ACCEPTANCE_OPERATOR_TOKEN={operator}\n"
            "Relay__StatePath=/state/rooms.json\nASPNETCORE_URLS=http://+:8080\n",
            encoding="utf-8",
        )
        environment.chmod(0o600)
        return context, app, certificates

    def resource(self, kind: str, name: str) -> None:
        {"container": self.containers, "network": self.networks, "volume": self.volumes, "image": self.images}[kind].append(name)

    def start(self, context: Path, certificates: Path) -> tuple[str, str, str]:
        assert self.temporary is not None
        image = self.prefix + ":test"
        for dependency in (SDK_IMAGE, RUNTIME_IMAGE, PROXY_IMAGE):
            self.command(self.docker + ["pull", dependency], timeout=600, show=True)
        self.resource("image", image)
        self.command(self.docker + [
            "build", "--label", f"{LABEL}={self.run_id}", "-t", image,
            "-f", str(context / "deploy" / "relay" / "Dockerfile"), str(context),
        ], timeout=900, show=True)
        self.check("current Relay Dockerfile builds from the minimal context")
        self.digests()
        network = self.prefix + "-network"
        self.resource("network", network)
        self.command(self.docker + ["network", "create", "--label", f"{LABEL}={self.run_id}", network])
        subnet = self.command(self.docker + ["network", "inspect", network, "--format", "{{(index .IPAM.Config 0).Subnet}}"])
        owner = self.command(self.docker + ["network", "inspect", network, "--format", "{{index .Labels \"" + LABEL + "\"}}"])
        if owner != self.run_id:
            raise AcceptanceFailure("Refusing to reconfigure a network without this run's ownership label.")
        # Reuse Docker's conflict-free choice, but explicitly declare it for the fixed trusted-proxy IP.
        self.command(self.docker + ["network", "rm", network])
        self.command(self.docker + ["network", "create", "--label", f"{LABEL}={self.run_id}", "--subnet", subnet, network])
        proxy_ip = str(ipaddress.ip_network(subnet).network_address + 10)
        state = self.prefix + "-state"
        probe = self.prefix + "-probe"
        for volume in (state, probe):
            self.resource("volume", volume)
            self.command(self.docker + ["volume", "create", "--label", f"{LABEL}={self.run_id}", volume])
        relay = self.prefix + "-relay"
        self.resource("container", relay)
        self.command(self.docker + [
            "run", "-d", "--name", relay, "--label", f"{LABEL}={self.run_id}",
            "--network", network, "--network-alias", "relay", "--read-only", "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges:true", "--memory", "256m", "--cpus", "1", "--pids-limit", "128",
            "--tmpfs", "/tmp:size=32m,mode=1777", "--mount", f"type=volume,src={state},dst=/state",
            "--env-file", str(self.temporary / "private.env"), "--env", "Relay__KnownProxies__0=" + proxy_ip,
            "--health-cmd", "dotnet HelL.Relay.dll --healthcheck", "--health-interval", "2s",
            "--health-timeout", "5s", "--health-retries", "20", image,
        ])
        self.wait_healthy(relay)
        self.command(self.docker + ["exec", relay, "sh", "-ec",
            'test "$(id -u)" -ne 0; test -w /state; printf acceptance > /state/.acceptance; rm /state/.acceptance'])
        self.command(self.docker + ["exec", relay, "dotnet", "HelL.Relay.dll", "--healthcheck"])
        self.check("Relay runs non-root, can write its state volume, and passes its native healthcheck")
        nginx = self.temporary / "nginx.conf"
        nginx.write_text(
            "map $http_upgrade $connection_upgrade { default upgrade; '' close; }\n"
            "server {\n listen 443 ssl;\n server_name relay-proxy;\n"
            " ssl_certificate /certificates/server.crt;\n ssl_certificate_key /certificates/server.key;\n"
            " ssl_protocols TLSv1.2 TLSv1.3;\n access_log off;\n"
            " location / {\n proxy_pass http://relay:8080;\n proxy_http_version 1.1;\n"
            " proxy_set_header Host $host;\n proxy_set_header Authorization $http_authorization;\n"
            " proxy_set_header X-Forwarded-For $remote_addr;\n proxy_set_header X-Forwarded-Proto https;\n"
            " proxy_set_header Upgrade $http_upgrade;\n proxy_set_header Connection $connection_upgrade;\n"
            " proxy_read_timeout 180s;\n }\n}\n", encoding="utf-8",
        )
        proxy = self.prefix + "-proxy"
        self.resource("container", proxy)
        self.command(self.docker + [
            "run", "-d", "--name", proxy, "--label", f"{LABEL}={self.run_id}",
            "--network", network, "--network-alias", "relay-proxy", "--ip", proxy_ip,
            "--mount", f"type=bind,src={nginx},dst=/etc/nginx/conf.d/default.conf,readonly",
            "--mount", f"type=bind,src={certificates / 'server.crt'},dst=/certificates/server.crt,readonly",
            "--mount", f"type=bind,src={certificates / 'server.key'},dst=/certificates/server.key,readonly",
            PROXY_IMAGE,
        ])
        self.command(self.docker + ["exec", proxy, "nginx", "-t"])
        return relay, network, probe

    def wait_healthy(self, relay: str) -> None:
        for _ in range(90):
            status = self.command(self.docker + ["inspect", relay, "--format", "{{.State.Health.Status}}"])
            if status == "healthy":
                return
            if self.command(self.docker + ["inspect", relay, "--format", "{{.State.Running}}"]) != "true":
                raise AcceptanceFailure("The isolated Relay container exited before becoming healthy.")
            time.sleep(1)
        raise AcceptanceFailure("The isolated Relay container did not become healthy within 90 seconds.")

    def phase(self, phase: str, network: str, probe: str, app: Path, certificates: Path) -> None:
        assert self.temporary is not None
        name = self.prefix + "-" + phase
        self.resource("container", name)
        launch = f"dotnet /app/HelL.Relay.Acceptance.dll {phase}"
        if phase != "untrusted":
            launch = "update-ca-certificates >/dev/null && " + launch
        self.command(self.docker + [
            "run", "--rm", "--name", name, "--label", f"{LABEL}={self.run_id}", "--network", network,
            "--env-file", str(self.temporary / "private.env"),
            "--mount", f"type=bind,src={app},dst=/app,readonly",
            "--mount", f"type=bind,src={certificates / 'ca.crt'},dst=/usr/local/share/ca-certificates/hell-acceptance.crt,readonly",
            "--mount", f"type=volume,src={probe},dst=/probe",
            "--mount", f"type=volume,src={self.prefix}-state,dst=/relay-state,readonly",
            SDK_IMAGE, "sh", "-ec", launch,
        ], timeout=300, show=True)
        self.check("container probe phase: " + phase)

    def digests(self) -> None:
        hashes = self.report["imageDigests"]
        assert isinstance(hashes, dict)
        for image in (self.prefix + ":test", SDK_IMAGE, RUNTIME_IMAGE, PROXY_IMAGE):
            metadata = self.command(self.docker + ["image", "inspect", image, "--format", "{{json .}}"])
            parsed = json.loads(metadata)
            hashes[image] = {"id": parsed["Id"], "repoDigests": parsed.get("RepoDigests") or []}

    def cleanup(self) -> None:
        cleanup = self.report["cleanup"]
        assert isinstance(cleanup, list)
        for kind, names in (
            ("container", self.containers), ("network", self.networks),
            ("volume", self.volumes), ("image", self.images),
        ):
            template = "{{index .Config.Labels \"" + LABEL + "\"}}" if kind in {"container", "image"} else "{{index .Labels \"" + LABEL + "\"}}"
            for name in reversed(names):
                inspected = subprocess.run(self.docker + [kind, "inspect", name, "--format", template],
                    capture_output=True, text=True, timeout=30, check=False)
                if inspected.returncode:
                    error = inspected.stdout + inspected.stderr
                    if name in error and ("No such" in error or "not found" in error):
                        cleanup.append({"kind": kind, "name": name, "alreadyAbsent": True})
                        continue
                    raise AcceptanceFailure("Could not verify ownership during cleanup: " + self.redact(error[-2000:]))
                if inspected.stdout.strip() != self.run_id:
                    raise AcceptanceFailure("Refusing to clean a resource without this run's ownership label.")
                remove = self.docker + [kind, "rm"] + (["-f"] if kind == "container" else []) + [name]
                self.command(remove)
                cleanup.append({"kind": kind, "name": name, "removed": True})
        if self.temporary is not None and self.temporary.exists():
            base = Path(tempfile.gettempdir()).resolve()
            if self.temporary.parent != base or not self.temporary.name.startswith("hell-relay-acceptance-"):
                raise AcceptanceFailure("Refusing to remove an unrecognized temporary directory.")
            shutil.rmtree(self.temporary)
            cleanup.append({"kind": "temporary directory", "removed": True})

    def execute(self, context: str | None) -> int:
        failure: BaseException | None = None
        try:
            self.local_engine(context)
            build_context, app, certificates = self.prepare()
            relay, network, probe = self.start(build_context, certificates)
            self.phase("untrusted", network, probe, app, certificates)
            self.phase("seed", network, probe, app, certificates)
            self.command(self.docker + ["restart", relay])
            self.wait_healthy(relay)
            self.phase("after-restart", network, probe, app, certificates)
            self.digests()
        except (AcceptanceFailure, OSError, subprocess.TimeoutExpired, ValueError, KeyboardInterrupt) as error:
            failure = error
            print(self.redact(str(error)), file=sys.stderr, flush=True)
        finally:
            try:
                self.cleanup()
            except (AcceptanceFailure, OSError, subprocess.TimeoutExpired) as error:
                failure = failure or error
                print("Cleanup failed: " + self.redact(str(error)), file=sys.stderr)
            self.report["status"] = "failed" if failure else "passed"
            if failure:
                self.report["error"] = self.redact(str(failure))
            self.report_path.parent.mkdir(parents=True, exist_ok=True)
            self.report_path.write_text(json.dumps(self.report, indent=2) + "\n", encoding="utf-8")
        return 1 if failure else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--docker-context", help="Optional local Docker context; remote engines are rejected.")
    parser.add_argument("--report", type=Path, default=ROOT / "artifacts" / "relay-acceptance" / "report.json")
    args = parser.parse_args()
    return RelayAcceptance(args.docker_context, args.report.resolve()).execute(args.docker_context)


if __name__ == "__main__":
    raise SystemExit(main())
