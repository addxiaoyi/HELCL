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

Final local counts and GitHub run/artifact receipts are recorded after execution.
Previous green runs are a baseline, not evidence for this changed implementation.

## Scope boundaries

This round changes backend code and tests. It does not add a new UI design or
claim Android-device, macOS-ARM, public dual-device or production acceptance.
Container tests run in the hosted runner's isolated private network. The local
Docker Desktop recovery limitation from the previous checkpoint remains separate.
