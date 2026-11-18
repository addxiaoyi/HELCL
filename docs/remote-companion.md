# Remote companion backend

The remote path is opt-in. It forwards the existing launcher's task snapshot and
task-cancellation commands through its protected local pipe, an inner pinned TLS
connection and the private relay. It never creates a second `BackendApi` owner.
Both the launcher and the paired device must remain online.

## Components

- `RemotePairingStore` creates a stable device identity and persists explicit
  certificate pins and per-device permissions. The JSON file is AES-GCM encrypted;
  its short encryption key lives in an injected system secret store. A failed
  commit leaves the previous state and key usable. Concurrent stale writers fail
  instead of overwriting another session's pairing changes.
- `PairedRemoteHost` admits only explicitly paired devices, rechecks permissions
  before each command, reconnects after transport loss, publishes renewed room
  invitations and stops active sessions on permission changes or revocation.
  It renews every 20 hours by default, or when fewer than two minutes remain.
- `PairedRemoteClient` checks the invitation against the confirmed host pin and
  intended client identity before connecting. Bounded connection retries retain
  the same request ID so a lost cancellation receipt cannot repeat the operation.
- `InvitationFeedPublisher` encrypts renewed invitations with a separate AES-GCM
  capability key. The relay stores only a reader-token hash and ciphertext.
  `InvitationFeedClient` reads through HTTPS with a separate reader token and
  validates the channel, revision, host/client pairing and original relay before
  accepting an update. Operator credentials and host tickets never reach it.
- `CompanionRelayHost` sends commands to the existing `HostPipeServer` using its
  current-user pipe and session token. The pipe permission and paired-device
  permission must both allow an operation.
- `BridgeHost.StartRemoteCompanionAsync` starts the .NET 10 helper from the .NET
  Framework launcher. Bootstrap credentials travel over private standard input,
  not process arguments. Stopping the helper closes its local pipe too.

The protocol remains limited to `Snapshot` and `CancelTask`. Pairing does not
grant arbitrary RPC access, game control, screen capture or file-system access.

## Build and pair devices

Build or publish the companion helper separately from the WPF executable:

```powershell
dotnet build tools/HelL.Companion.Host -c Release
dotnet publish tools/HelL.Companion.Host -c Release -r win-x64 --self-contained true -o publish/companion-win-x64
```

The CLI supports these commands; use the same data directory when pairing and
serving. `identity` exports only a public device ID, name and certificate
fingerprint. Exchange those public offers over a trusted channel and compare the
fingerprints on both devices before running `trust`.

```text
HelL.Companion.Host identity <data-root> <device-name>
HelL.Companion.Host list <data-root> <device-name>
HelL.Companion.Host trust <data-root> <device-name> <public-offer-file> <confirmed-fingerprint> [--allow-cancel]
HelL.Companion.Host revoke <data-root> <device-name> <paired-device-id>
```

Trust defaults to read-only access. Cancellation requires `--allow-cancel` plus a
launcher host pipe that explicitly grants `CancelTasks`. Changing a pinned
certificate requires revoking the previous device first. A missing system key,
corrupt pairing file or invalid certificate fails closed; do not silently replace
an existing identity. Re-pair after replacing the identity or its certificate.

## Launcher integration

After pairing, the existing WPF `BridgeHost` can start its helper:

```csharp
RemoteCompanionSession session = await bridge.StartRemoteCompanionAsync(
    companionExecutable, pairedDeviceId, relayBaseUri, operatorToken,
    new[] { HostPermission.Read }, cancellation);
string invitation = session.InvitationJson;
```

Only the local host receives the relay operator token and pipe token. The helper's
private standard-output stream carries the client invitation, including the
client ticket. Deliver that invitation only to the already paired client; never
write it to logs, analytics or a public file. Its `Host` offer is verified against
the client's existing trust, rather than becoming an automatic trust decision.

`ReadRenewedInvitationAsync` returns the newest invitation after renewal.
Cancelling that wait leaves the host running; a single persistent reader prevents
cancelled callers from consuming a later invitation. EOF or malformed helper
output becomes an explicit error. Call `StopRemoteCompanion` to end remote hosting.

The helper now publishes encrypted invitation updates automatically. The initial
invitation includes a `Feed` capability which must travel over the same trusted
private channel as the client ticket. The capability expires after seven days;
an expired subscription needs a fresh initial invitation. Publisher metadata is
kept in the host's system secret store, so a normal helper restart can reuse the
subscription. Revocation attempts remote cleanup even if the local key store is
locked, and records incomplete cleanup as an explicit error.

## Mobile pairing

Open **My → Remote computer** in the mobile UI. Exchange only the public offers,
compare the displayed SHA-256 fingerprints on both devices and explicitly confirm
trust. Permissions default to read-only; cancellation also requires the computer
to grant that phone permission. Import the private client invitation after trust
has been confirmed. Private input is concealed and cleared after import.

The page shows loading, empty, task, failure and cancelled states. It supports
snapshot refresh, explicitly permitted task cancellation, local trust revocation,
and pausing/resuming automatic invitation updates. Only public identity/trust is
persisted through the mobile system key store; the private invitation stays in
the process session. After an app process restart, import a private invitation
again. Within a session, older update revisions and late replies from a previous
host/subscription cannot overwrite the current state.

For a .NET 10 caller, use `CompanionRelayHost.Connect` directly and then
`PairedRemoteHost.StartAsync`. Its publishing callback receives the initial and
renewed invitations. `RenewAsync` revokes the old room and replaces its tickets;
`RevokeAsync` removes durable local trust, cancels the active host and revokes
relay routing. Direct changes to the pairing file are noticed within one second,
and trust is refreshed before every command.

The helper's `serve <root> <name> <paired-device-id> <relay-url>` mode receives one
bootstrap JSON line on private stdin. The launcher supplies it automatically.
Subsequent `renew`, `revoke` and `stop` lines control the session; closing stdin
also stops it. Applications should consume this stream through
`RemoteCompanionSession`, rather than treating it as a public terminal log.

## Verification and deployment boundaries

The local integration test runs a real loopback relay and protected Windows pipe.
It reads a task owned by the existing launcher, rejects cancellation for a
read-only device, allows an explicitly granted cancellation and revokes the
session. Other tests cover encrypted identity restart, failed commits, changed
pins, automatic renewal, old-ticket rejection, external revocation and shutdown
during blocked invitation publication.

The cross-runtime verifier additionally passed with a real net48 owner and net10
client under an isolated child-process temporary directory. It checked the local
snapshot, session-token rejection, explicit permissions, exclusive root lease and
clean shutdown. The default temporary directory was inaccessible in that run;
the verification did not change directory ACLs or weaken the lease.

```powershell
dotnet test tests/HelL.Remote.Tests -c Release
dotnet test tests/HelL.Portable.Tests -c Release
dotnet test tests/HelL.Tests -c Release --filter FullyQualifiedName~RemoteCompanionSessionTests
```

See `deploy/relay/README.md` for HTTPS, trusted proxy configuration, persistent
revocation state and resource limits. The mobile pairing page and encrypted
invitation-delivery service are implemented. GitHub workflows separately exercise
native Linux/macOS credentials and containerized HTTPS/WSS traversal. A runner's
isolated container network is not two physical machines or a public deployment;
real Android device behavior and public cross-machine operation remain distinct
acceptance boundaries.

## Rollback

Stop companion sessions before replacing binaries. Ship the RPC server and client
together because overflow recovery relies on the new `events_reset` handling.
Preserve launcher data, the OS key store and relay `/state` volume. Pairing data
uses its own `remote-pairing.json` file and does not migrate account data. Rolling
back the helper leaves that file intact. Restore the prior application binaries
to undo these code changes; do not delete user credentials or accept old revoked
room tickets as a rollback shortcut.
