# Backend reliability continuation — 2026-11-18

This follows the verified `83e2b2b` test snapshot documented in
`remote-followup-2026-11-18.md`. It preserves the original dirty workspace and
uses another dedicated GitHub test branch; it does not authorize a main merge,
release, production deployment or changes to unrelated machines.

## Changes

- **Account session ownership:** login, restore and refresh results cannot commit
  after logout or replace a newer session, including another login with the same
  UUID. Refresh token use is serialized with cancellable waiting. Network work
  stays outside the account state lock; local persistence and logout commit are
  serialized, so a slow system key store can delay same-instance state access.
- **Uncertain native writes:** a new encryption-key write is considered attempted
  before calling the provider. If it commits and loses its receipt, compensation
  removes that new key. Independent cleanup finally blocks prevent a temporary
  file error from skipping key cleanup. Unsuccessful physical cleanup is reported.
- **HTTP/RPC boundaries:** invalid framing and unconsumed bodies close the
  connection, rather than interpreting body bytes as another request. Request
  headers have a 65,536-byte aggregate wire budget. Invalid explicit JSON-RPC
  versions, IDs, params types, duplicate members, non-finite float conversion and
  excessive nesting are rejected before dispatch. Unversioned legacy requests,
  valid finite floats and arbitrary-size integer behavior remain supported.
- **Pairing snapshot rotation:** envelope handles close before native key lookup.
  A missing old key causes a bounded reload only when the saved KeyId has actually
  changed. A truly missing key or corrupt pairing file still fails closed.
- **Durable invitation revocation:** an unconfirmed remote DELETE retains the
  durable revocation record. Reopening retries retirement before saving or
  publishing a fresh channel. Detected snapshot drift prevents stale writers from
  rolling back a revision or deleting a newer secret. The existing secret-store
  interface does not offer cross-process atomic CAS, and this is not claimed.
- **Hosting failure visibility:** internal renewal/publication cancellation is
  reported as a hosting failure. Explicit owner stop/revoke/dispose remains normal
  shutdown. The helper no longer classifies every cancellation after its own
  cleanup as success, and synchronous console input cannot block its shutdown.
- **Download completion and install promotion:** cancellation is checked before
  a batch, after progress callbacks and before/after terminal success is reported.
  Batch failures end tracker state as failed. A verified replacement is promoted
  with the existing target preserved on failure; Unix uses replace-in-place when
  the target exists, and a cross-filesystem replacement refusal leaves both files
  untouched.
- **Named-pipe boundary:** host hello/request/reply fields are required and duplicate
  wire keys rejected without removing DTO defaults for source compatibility.
  A pre-authentication peer has a separate five-second budget; authenticated
  command sessions retain their bounded timeout. Linux-runner tests report actual
  Unix-backed pipe execution separately from Windows-only ACL tests.
- **Remote lifecycle stress:** an opt-in bounded runner drives real loopback
  WebSockets, pinned TLS and the hosted relay through repeated client sessions,
  renewals, restarts, revocations and feed recovery. Receipts record seeds, cycle
  counters, peak resources, errors and final cleanup; it never reports a public
  two-device test.

## Failure-first evidence

Each change has deterministic failure-first coverage: controlled HTTP/key-store
gates for late account results, a provider that writes then throws, raw sockets
with owner-state assertions for RPC boundaries, snapshot-rotation callbacks,
failed DELETE/reopen sequences, and cancellation-specific host/helper tasks.
Existing rollback, send-count, permission, validity and keep-alive assertions
were retained. Test outputs are under `TestResults/backend-next`,
`TestResults/backend-hardening/account-session-next-*`,
`TestResults/rpc-boundary-20261118` and `TestResults/remote-recovery-20261118`.

The native credential verifier also performs an actual system key write followed
by an injected lost receipt, then verifies old account bytes/key usability and
that the new native key was compensated. Its report adds
`lost_key_receipt_preserves_state`; temporary credential/keychain cleanup remains
mandatory for success.

## Verification

The final local gate for this continuation passed:

- Remote: **89/89** (`TestResults/backend-next/remote-all.trx`).
- Current Remote snapshot additionally has **89/89** including revocation recovery
  and host-lifecycle regressions (`artifacts/backend-next/remote-all/bin/HelL.Remote.Tests/release/HelL.Remote.Tests.dll`; detailed TRX is in `TestResults/backend-next`).
- Portable: **275/275** (`TestResults/backend-next/portable-final.trx`).
- Current Portable full suite: **275/275**, including the 35 RPC boundary,
  Companion process-loop and uncertain-key-cleanup tests
  (`TestResults/backend-next/portable-final.trx`).
- Original suite: **816/816** (`TestResults/backend-next/legacy-final.trx`),
  with its net48 WPF output built at **0 warnings / 0 errors**. The new Companion
  process-loop tests are included in the Portable suite.
- Mobile: **28/28** (`TestResults/backend-final/mobile-final.trx`).
- Download recovery passed **7/7 on Windows and 7/7 on Linux**, including
  cancellation, HTTP verification interruption, failed file promotion and a
  real cross-filesystem refusal case (`TestResults/backend-hardening/download-recovery-*`).
- IPC boundary passed **28/28 Windows cases** and **8 Linux executed / 6 explicit
  Windows-only skips**; the clean net48 build reported **0 warnings/errors**
  (`TestResults/ipc-boundary-20261118`).
- The 12-cycle/4-pair loopback stress completed 12 Relay restarts with no errors,
  176 snapshots, 48 cancellations, 48 read-only denials, 48 revocation denials
  and 48 feed recoveries; secrets/tasks/final workspaces were cleaned
  (`TestResults/remote-stress-20261118/soak-12x4-seed42.json`).
- Companion helper build succeeded; the pre-existing Core analyzer warnings remain.
- Native WSL key store: **8/8 checks**, including `lost_key_receipt_preserves_state`,
  signature/export identity recovery and native/temp cleanup:
  `artifacts/backend-next/native-linux-report.json`.
- GitHub `codex/remote-followup-ci-20261118-02`: run `44332333025` passed on
  Ubuntu 24.04 and Intel macOS 15 with the native system key store and the lost
  write-receipt check. Both reports passed all eight native checks, identity
  signing/export/reimport and cleanup. The tested code head is
  `938f12c7716dd7a2716be144672e668497c1933b`.
- GitHub Relay run `44332332907` passed on the same code head: **89/89** Remote
  tests and all seven actual container checks. Four image metadata records and
  ten owned-resource cleanup entries were downloaded and verified.
- GitHub Mobile run `44334620638` passed on final documentation head
  `38b5d0cd3fcb442a6d672b4ecb3cd08084a2d640`: **28/28** Mobile, **275/275**
  Portable, both builds and three preview sizes passed. The downloaded artifact
  contains 24 screenshots; all three reports are explicitly desktop-hosted,
  fixture-transport checks with Android-device verification false.
- Remote/code boundary and account changes were failure-first, with owned TRX
  evidence under `TestResults/backend-next`, `TestResults/rpc-boundary-20261118`,
  `TestResults/remote-recovery-20261118` and
  `TestResults/backend-hardening/account-session-next-*`.

All implementation and regression fixes described above are in the tested
`938f12c` code commit. Its successor `38b5d0c` changes only this Markdown record
and `.ci/remote-followup-source.json`; source, tests, tools, workflows and deployment
inputs were compared and are unchanged. Native and Relay results are therefore
attributed to their exact code commit, while the additional Mobile run confirms
the final documentation head. No further commit or CI cycle is required for this
local evidence update. Consolidated metadata and checks:
`TestResults/backend-next/github-final.json`.

GitHub runs:

- [Native 44332333025](https://github.com/addxiaoyi/HELCL/actions/runs/44332333025)
- [Relay 44332332907](https://github.com/addxiaoyi/HELCL/actions/runs/44332332907)
- [Final-head Mobile 44334620638](https://github.com/addxiaoyi/HELCL/actions/runs/44334620638)

## Scope boundaries

This round changes backend code and tests. It does not add a new UI design or
claim Android-device, macOS-ARM, public dual-device or production acceptance.
Container tests run in the hosted runner's isolated private network. The local
Docker Desktop recovery limitation from the previous checkpoint remains separate.

The finalized update is on `codex/remote-followup-ci-20261118-02` only. The
original checkout remains on its existing dirty `master`; `main` is not merged or
deployed.
