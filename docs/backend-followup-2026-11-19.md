# Backend follow-up — 2026-11-19

This incremental layer depends on Draft PR #1 (`codex/remote-followup-ci-20261118-01` →
`...-02`), which establishes the HelL source snapshot absent from the old `main`.
This layer adds download/install reliability, protected IPC boundaries, a repeatable
remote lifecycle soak, and a verifiable full WPF Release build. It preserves the
original dirty workspace and does not merge to `main` or deploy production.

## Download and install

- Batch cancellation is now reported as a failed/cancelled batch even if it
  arrives before an empty batch, from the initial progress callback, at the final
  file progress callback or immediately before terminal success.
- A verified temporary download is promoted without deleting the installed copy
  first. On Unix, an existing destination uses `File.Replace`; failure, including a
  cross-filesystem replace refusal, preserves both source and existing target. An
  absent destination uses non-overwriting `File.Move`. This guarantees fail-safe
  preservation, not universal atomicity for cross-filesystem promotion.
- Deterministic HTTP download fixtures prove a post-verification missing part
  cannot destroy the old installed file. Cross-filesystem Linux coverage confirms
  `/dev/shm` and the workspace are different devices before the test runs.

Windows download gates: 7/7. Linux download gates: 7/7. Evidence is under
`TestResults/backend-hardening/download-completion-next.trx` and
`download-recovery-linux-next-{red,green,full}.trx` / Windows equivalents.

## Protected IPC

- The helper now requires explicit hello/request/reply fields and rejects duplicate
  wire fields before dispatch. Public DTO defaults remain available to in-process
  callers.
- The pre-authentication hello has a 5-second limit; authenticated sessions retain
  the 30-second command limit. Bad cancellation frames do not cancel owner tasks.
- Windows real-pipe IPC: 28/28. Linux actual pipe tests: 8 executed passed, plus 6
  Windows ACL-only cases explicitly skipped. The complete Windows build for the
  net48 companion verifier passed with 0 warnings/errors.

Evidence: `TestResults/ipc-boundary-20261118/`.

## Repeatable remote stress

`tools/HelL.Remote.Stress` uses a real local TCP/WebSocket relay and pinned inner
TLS 1.3, with isolated task and secret fixtures. One cycle covers pairing, snapshot,
permitted cancellation, renewal and old-ticket rejection, relay restart with saved
state, encrypted invitation feed recovery, read-only denial and revocation. Reports
include the seed, successful action counters, peak memory/threads/handles and final
resource cleanup; credentials and exception payloads are omitted. The tool never
uses Docker, public endpoints, native user keyrings or an existing launcher
directory.

Local 12-cycle × 4-client soak, seed 42: 12 relay restarts, 176 snapshots, 48
cancellations, 48 read-only denials, 48 revocation denials and 48 feed recoveries;
errors=0. Final host/relay/workspace cleanup passed and fixture secrets/tasks=0.
Receipt: `TestResults/remote-stress-20261118/soak-12x4-seed42.json`; safeguards:
12/12. This is bounded loopback lifecycle pressure, not public dual-device or
sustained throughput evidence.

## Full application build and PR relationship

The full solution Release build initially exposed missing `HelL.WallpaperCapture`
solution configuration and a parallel project-instance write collision. The
solution now maps its Debug/Release target and orders the net48 tests after the WPF
launcher build. A fresh, parallel Release solution build succeeded with 0 warnings
and 0 errors. The embedded wallpaper DLL SHA matches the Release output. Evidence:
`TestResults/backend-delivery/full-wpf-mapped-release-final.log` and
`full-wpf-embedded-resource.json`.

Additional native/portable/desktop/service build logs are under
`TestResults/backend-delivery`; all succeed with 0 errors. Existing Core platform
and obsolete API warnings are preserved where they occur.

The second Draft PR is based on PR #1's test branch. The public `main` still has
the older TipCL tree. The full HelL GUI files in this workspace are untracked and
are not in that compact backend snapshot; the complete WPF build is local
workspace-foundation evidence. PR-B CI builds/tests the shared backend/Companion,
the backend regression suites and a bounded 3-cycle/2-pair stress check. Public
network, macOS ARM and Android-device acceptance remain separate boundaries.
