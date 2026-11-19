# Remote and diagnostic features — 2026-11-19

This increment starts at PR #2's `be264e4` and preserves the original dirty
workspace. It adds the five requested features without merging or deploying.

## Delivered behavior

1. Remote task progress includes its stage, measured speed string, state, title,
   outcome and timestamps. Details expose a bounded redacted log separately from
   the snapshot. Snapshots prioritize running tasks, show at most 40 records and
   explicitly report truncation; the owner retains at most 80 task records and
   20 log lines per task, each no longer than 512 characters.
2. Phone invitations, feed capabilities, accepted revisions and selected host are
   saved inside the existing encrypted pairing envelope. The native secret store
   retains only its AES key. Opening the remote page restores the selected host;
   expired room credentials wait for a verified feed update before transport use.
3. Each paired computer has its own invitation and last successful read time.
   Switching cancels outstanding work, clears the previous host's private view,
   and restores the selected connection. Revocation deletes that host's saved
   invitation, selection, activity and revision. Older pairing envelopes remain
   compatible. Only the selected host's update feed is polled.
4. Read-only game status is separate from game control. A confirmed graceful
   window-close request requires the current session ID and `ControlGame` on both
   the pairing and local pipe. Task cancellation cannot invoke process killing.
   Retried requests are idempotent; old sessions cannot close a newly bound game.
   A close request does not promise that the game saved or exited. A game without
   a closeable window stays alive and returns a clear refusal. New confirmations
   can retry after ten seconds if a window ignores or cancels the first request.
5. WPF, Avalonia Desktop and Mobile have diagnostic-preview/export entries.
   Users choose sections and individual fields before saving a single-file ZIP.
   Reports contain at most 16 KiB of approved environment/check summaries. They
   contain no accounts, raw paths, credentials or raw logs and are never uploaded.
   Desktop replacement failures/cancellation preserve the previous destination;
   mobile document-provider failures are explicit but cannot promise atomicity.

## Verification

Local results are under `D:/qwq/qdq/TestResults/remote-features/`:

- Portable: **331/331**, including diagnostic boundaries, task-state limits,
  permission checks and three actual Windows process/window tests.
- Remote: **97/97**, including invitation persistence and read-only transport
  denial of game-stop requests through real loopback WebSockets/pinned TLS.
- Mobile: **40/40**, including restart, expired-ticket recovery, cross-host late
  replies, revocation, persistence failure and safe refusal messages.
- Existing suite: **816/816**.
- Full original-workspace WPF solution Release: **0 warnings / 0 errors**.
- Bounded loopback lifecycle: three cycles/two clients, seed 20261119, three Relay
  restarts and zero errors; owned processes, temporary state and secrets cleaned.
- Mobile preview reports and screenshots cover task details, game status/close
  confirmation, multiple hosts, narrow/wide screens and diagnostic selection.
  These use desktop-hosted fixture transport, not a physical Android phone.

The initial blocked-write pipe fixture assumed unbounded titles. It was adapted
to fill the new bounded snapshot with escaped text, retaining the blocked-write
and disposal assertions. A transient stress initialization failure with the
default Windows temp directory was resolved by using this run's owned temp root;
the successful receipt is `stress-3x2-final.json`.

The first macOS CI run exposed an existing fixture name exceeding the platform's
104-byte Unix socket path limit. The pipe fixture now uses its full random UUID
without the redundant prefix; its read/write cancellation and partial-frame
assertions remain unchanged.

## Source and acceptance boundaries

The compact PR base lacks the broader WPF/Avalonia GUI foundation. New diagnostic
panels are included here; the two minimal settings-entry edits are preserved in
`gui-feature-entrypoints.patch` and applied to the original workspace. Complete
WPF/Desktop/Android builds therefore verify that workspace foundation, not a GUI
migration contained in this compact PR. Its backend, Mobile and Companion build
and regression paths are self-contained.

The phone manages several computers; the WPF helper still serves one selected
phone at a time. Only game status and graceful close were added, not remote game
launching, file access or arbitrary commands. Physical Android, public two-device
HTTPS/WSS, macOS ARM and production deployment remain separate acceptance steps.
