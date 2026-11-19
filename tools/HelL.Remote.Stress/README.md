# Remote lifecycle soak

This tool starts its own real loopback HTTP/WebSocket relay, runs pinned inner
TLS 1.3 connections, and restarts only that relay. It never starts Docker,
contacts a public relay, uses the native OS credential store, or opens an
existing launcher data directory. Its task owner and secret store are isolated
fixtures. This is transport/backend lifecycle pressure, not two physical public
Internet devices or full launcher task execution.
Relay restart means rebuilding its WebApplication service on the same loopback
port in this process; it is not an OS process kill or power-loss simulation.

Short smoke:

```powershell
dotnet run --project tools/HelL.Remote.Stress --artifacts-path artifacts/remote-stress-build -- --cycles 3 --pairs 2 --seed 20261118 --report artifacts/remote-stress/report.json
```

Long soak (180 cycles across at least three hours):

```powershell
dotnet run --project tools/HelL.Remote.Stress --artifacts-path artifacts/remote-stress-build -- --cycles 180 --pairs 2 --duration 03:00:00 --seed 42 --report artifacts/remote-stress/soak.json
```

`--period HH:MM:SS` sets a minimum cycle period instead of `--duration`.
Pacing occurs after a cycle's sessions have been cleaned up; it is a finite
lifecycle churn test, not a sustained-throughput benchmark. Stages default to
a 60-second deadline, configurable with `--timeout` (5 seconds to 5 minutes).
The seed changes peer order and snapshot wave counts; all authentication
credentials and private keys still use cryptographic randomness.

Every cycle reopens pairing identities, trusts clients, connects and publishes,
performs concurrent per-peer snapshots and task cancellations, renews rooms,
rejects old tickets, stops/restarts the relay on the same loopback port with its
state intact, reopens publishers and decrypts newer feed revisions, verifies a
read-only grant cannot cancel a task, and revokes both routing and reader access.
The revoked request must neither return success nor execute the task owner.

Unexpired room/feed tombstones deliberately remain in the registry. The fixture
allows at most 1,000 total pairing generations, 1,024 feed records and 4,096 room
records, so a long run does not hide policy capacity failures by deleting live
tombstones. Only management and anonymous request budgets are raised inside this
isolated fixture; role concurrency, per-role connection and reader limits stay
enabled. Each cycle uses a separate host coordinator and cleans its task rows.

The JSON receipt records cycles, seed, completed actions, real relay restarts,
errors (without exception payloads), peak RSS/managed heap/threads/handles/secret
entries, peak tracked room/feed records, and final host/relay/workspace cleanup.
Temporary directories require a unique ownership marker before recursive removal.
Owned in-memory secrets are explicitly cleared; this does not claim native
key-ring deletion. Exit 0 means all cycles and cleanup passed, 1 means failure,
and 2 means an explicit cancellation. Ctrl+C still attempts fixture cleanup.

Safety/configuration tests:

```powershell
dotnet test tests/HelL.Remote.Stress.Tests --artifacts-path artifacts/remote-stress-tests
```

On GitHub-hosted runners keep the requested duration within the job timeout.
An uploaded receipt with `passed` is evidence only for its recorded cycle count,
seed and scope; compile/unit-test success alone is not soak evidence.
