# Backend hardening — 2026-11-18

Workspace: `D:\qwq\qdq`. This records local source changes and verification, not
a release, remote commit, public deployment or Android device acceptance.

This is the earlier hardening checkpoint. Subsequent mobile pairing, encrypted
invitation delivery and actual GitHub Linux/Intel-macOS/container acceptance are
recorded in `remote-followup-2026-11-18.md`; the historical boundaries below should
not be mistaken for that follow-up's final status.

## Delivered scope

1. Restored the Portable regression gate by separating `DesktopTask` state from
   its Avalonia appearance partial. Tests still compile the production state
   model, without adding a GUI dependency or replacing it with a test substitute.
2. Made account mutation commit-first: failed add/replace/remove/select operations
   retain the previous in-memory collection, selection and saved credential
   references. Existing account files must be strictly readable and structurally
   valid before any sealing or compensation occurs. Logout clears the current
   in-memory session even if disk cleanup fails. The encrypted account envelope
   rejects invalid Base64, lengths, missing fields and invalid UTF-8 safely.
3. Bounded the local bridge to 64 connections, with separate five-second absolute
   header/body deadlines that cannot be extended by drip-fed bytes. SSE remains
   long-lived. Finished task titles are retained only with active/recent history.
4. Added independent WebSocket/TLS deadlines, trusted reverse-proxy handling,
   separate anonymous/operator budgets, signed-ticket entry budgets, pre-upgrade
   role reservations, bounded traffic/idle/registry usage and durable room
   revocation/renewal. Compose includes resource limits and a native health check.
5. Made RPC responses validate HTTP success, JSON-RPC version, matching ID,
   envelope uniqueness, result/error shape and result types. Queue overflow
   explicitly resets the SSE stream; the client reconnects and reconciles tasks
   from a snapshot instead of silently keeping stale state.
6. Added encrypted device identities and explicit certificate-pin trust,
   per-device permissions, host reconnection/renewal/revocation, paired clients,
   a protected-pipe bridge and a companion helper. The WPF launcher passes pipe
   and relay credentials over private stdin. A single bounded invitation reader
   survives cancelled waits and does not let stale startup cleanup stop a newer
   companion session. See `remote-companion.md` for the usable API and CLI.

## Failure-first evidence

- Account mutation, logout and malformed envelope tests failed against the
  previous implementation. Later independent review found that an unreadable
  old account file could make compensation delete still-live keyring entries;
  six strict-inventory regressions failed before that path was repaired.
- Bridge tests first reproduced unrestricted connections, unbounded title
  retention, silent overflow and ineffective request deadlines. Protocol tests
  first reproduced malformed response acceptance and default-value fallback.
- Relay tests first reproduced the wrong rejection status, missing revocation
  route and an entry-limit bypass by an authenticated non-WebSocket request.
- Review also covered disposal during blocked initial/renewal publication,
  external trust revocation, replacement-session ownership and cancellation of
  invitation reads. These checks use real local processes or relay connections
  where the boundary matters.

The `TestResults/backend-hardening` directory holds final TRX/log output plus
selected red/green evidence. It is local verification output, not a source file
that needs to be committed.

The final parallel run also exposed a relay cancellation interleaving: a completed
send could observe cancellation only at the next loop condition and return
normally. A deterministic send-callback regression failed first; the forwarder
now explicitly throws cancellation after that loop. Existing illegal-traffic
assertions were kept intact.

## Final local results

- Portable full suite: **223 passed, 0 failed** (`backend-portable-all.trx`).
- Remote full suite: **56 passed, 0 failed** (`backend-remote-all.trx`).
- Original full suite: **816 passed, 0 failed** (`backend-legacy-all.trx`).
- Mobile shared-code compatibility: **18 passed, 0 failed**
  (`backend-mobile-compat.trx`); this is not an Android device test.
- Companion stream/process lifecycle subset: **4 passed, 0 failed**
  (`backend-companion-session.trx`), also included in the original full suite.
- The real loopback relay / pinned TLS / protected pipe / existing task-owner
  integration is included in the Portable suite. It covers read-only denial,
  an explicitly permitted cancellation and revocation.
- Desktop and companion-helper builds passed. WPF/net48 was compiled as part of
  the clean original-suite build; `--help` on the actual companion executable
  exited successfully.
- Cross-runtime process acceptance passed with isolated child-process `TEMP/TMP`:
  the actual net48 owner and net10 client verified snapshots, rejected sessions,
  permissions, exclusive data-root leasing and clean shutdown. Evidence:
  `pipe-cross-runtime-isolated.log`, exit 0. Default `D:\UserCache\Temp` had an
  environment access restriction, also reproduced by net10 directory creation;
  no ACL or production locking code was relaxed. Verifier diagnostics now retain
  the exception cause chain without printing session credentials.
- Independent source review passed after fixing the strict account inventory,
  startup ownership and cancellation/renewal lifecycle findings. Scoped
  whitespace checks passed, and the CodeGraph index was synchronized.

## Reproduction

Use separate artifact roots if running suites in parallel. Projects share
`HelL.Contracts`, `HelL.Core` and `HelL.Remote`; using their default output roots
concurrently can produce file-lock compilation failures.

```powershell
dotnet test tests/HelL.Portable.Tests -c Release --artifacts-path artifacts/backend-hardening/portable-final -m:1 --logger 'trx;LogFileName=backend-portable-all.trx' --results-directory TestResults/backend-hardening
dotnet test tests/HelL.Remote.Tests -c Release --artifacts-path artifacts/backend-hardening/remote-final -m:1 --logger 'trx;LogFileName=backend-remote-all.trx' --results-directory TestResults/backend-hardening
dotnet test tests/HelL.Tests -c Release --artifacts-path artifacts/backend-hardening/legacy-final -m:1 --logger 'trx;LogFileName=backend-legacy-all.trx' --results-directory TestResults/backend-hardening
dotnet test tests/HelL.Mobile.Tests -c Release --artifacts-path artifacts/backend-hardening/mobile-final -m:1 --logger 'trx;LogFileName=backend-mobile-compat.trx' --results-directory TestResults/backend-hardening
dotnet build src/HelL.Desktop -c Release --artifacts-path artifacts/backend-hardening/desktop -m:1
dotnet build tools/HelL.Companion.Host -c Release -m:1
```

The initial default Desktop output was locked by an already-running .NET host.
The independent artifact build succeeded without stopping that application.
WPF/net48 was compiled through the clean legacy test artifact root. If reusing a
WPF output root, use `--no-incremental` after an interrupted Fody build. Existing
Core platform/obsolete API and shared-test nullable warnings remain; they were
not globally suppressed or presented as a clean-warning build.

## Remaining acceptance boundaries

- Docker Compose configuration validation passed with temporary fake secrets.
  Docker runtime acceptance could not start: the configured
  `tcp://127.0.0.1:2375` daemon rejected connections, and the explicit
  `desktop-linux` engine named pipe was absent. No image, container or volume was
  created. Image build, non-root state-volume permissions, health-check execution
  and container restart behavior therefore remain unverified.
- Local tests cover the real relay → pinned TLS → protected Windows pipe →
  existing launcher task owner path. They do not prove public HTTPS/WSS proxy
  traversal, cross-machine behavior, Android rendering or OS key-store behavior
  on Linux/macOS.
- The backend pairing/invitation API and helper are implemented. A mobile pairing
  screen and a network invitation-delivery service are outside this backend
  change; invitations must be delivered to the paired client through the owning
  application's trusted channel.
- No credentials were committed or used for a production operation. No Git
  commit, push, release publication or production deployment was performed.

## Rollback and resume

Preserve the dirty workspace and existing user data. Stop companion hosting
before replacing binaries; preserve OS key-store entries and the relay `/state`
volume. Keep the RPC client/server build together so stream-reset handling stays
compatible. The new pairing file is separate from existing account storage.

For container acceptance, first restore the local Docker daemon. Build from a
small temporary context containing only the three relay projects and Dockerfile,
excluding `bin`/`obj`; do not send this large workspace wholesale as build context.
Use isolated test names/volumes and fake secrets, verify health and persisted
revocation across restart, then remove only those test resources. No public
deployment is implied by completing that local check.
