# Backend lifecycle completion — 2026-11-19

This increment follows `2ffa1d9` (Draft PR #3). It improves the five existing
remote/diagnostic features through failure handling, terminal-state observation,
input validation, cancellation and repeatable acceptance. It does not merge or
deploy the application, enable arbitrary remote commands, or weaken TLS.

## Delivered behavior

- Visible remote pages refresh tasks and game status every five seconds, with
  backoff after failures. Refresh is opt-out, stops on leave/cancel/dispose and
  resumes through an explicit action. Editing pairing input pauses background
  reads. The game-close confirmation survives same-session rendering, but is
  cleared after sending, switching sessions or leaving the page. Successful
  close requests are followed to the stopped state instead of staying stale.
- Invitation feeds recover from transient network/HTTP failures and 429 responses
  using bounded backoff and Retry-After. Accepted revisions/ETags survive retries.
  Unauthorized readers, invalid JSON and authenticated ciphertext failures fail
  closed. Switching/revoking a host releases the old operation slot immediately;
  revision checks still reject late results. Activity is persisted at most once
  per minute, and a failed activity write cannot discard a successful snapshot.
- Remote TLS and local-pipe replies share semantic validation: outcome fields,
  identifiers, task count, lengths, progress, task-detail binding and game state.
  Remote wire decoding also rejects missing required/duplicate fields, including
  nested duplicates. A malformed reply cannot replace the phone's last valid view.
- Completed tasks ignore late progress; bounded history follows actual event
  order, and snapshot/truncation state is read under a single owner lock.
- Diagnostic collection/export freezes its selected preview, excludes concurrent
  exports, permits cancellation and remains usable after failure/picker cancel.
  Leaving/closing cancels waits and discards late success or failure callbacks.
  Waiting for the owner does not disable navigation or release another operation's
  semaphore. Chinese credential phrases and POSIX paths are also redacted.

## Evidence

Failure-first receipts include 9 rejected wire-boundary cases, late terminal
progress, 6 malformed mobile replies, transient-feed/host-switch/save-failure
cases, diagnostic field mutation during export, localized credential/path
redaction, and ignored-cancellation providers reporting late failures. Previous
assertions were retained; malformed-feed tests that already passed are recorded
as added coverage rather than attributed to a nonexistent fix.

Local combined gates under `TestResults/backend-close-loop/`:

- Portable **340/340**, including real Windows game-window/process tests.
- Remote **120/120**, including actual loopback WebSocket and pinned TLS tests.
- Mobile **50/50**, including recovery, persistence and malformed reply isolation.
- Full WPF Release solution: **0 warnings / 0 errors**.
- Avalonia Desktop Release and Android ARM64 publish succeed with the 16 existing
  Core warnings. These use the original GUI foundation, absent from the compact
  backend PR base. New files/edits were applied only after baseline/hash checks;
  original bytes are preserved in `TestResults/backend-close-loop/prechange/`.
- Enhanced desktop-hosted previews assert automatic terminal reconciliation,
  failed game-status recovery, confirmation clearing, polling pause, blocked
  request cancellation on leave, diagnostic export/retry/close behavior, and no
  late UI update. Fixture transport does not constitute Android-device proof.

CI keeps full transport testing on Windows/Linux, and explicitly limited pairing,
feed and protocol coverage on macOS because its native TLS server cannot provide
the required TLS 1.3. Native-key-store checks, three preview sizes, bounded stress
cleanup and isolated HTTPS/WSS container traversal are separate checks.

## Remaining external acceptance

Physical Android behavior, public two-machine HTTPS/WSS and macOS ARM still need
their actual environments. The WPF helper serves one selected phone. Diagnostic
Android document providers do not universally support atomic overwrite. A
cancelled synchronous system/mirror probe can finish its current bounded stage
before releasing the owner; it does not block UI navigation.
