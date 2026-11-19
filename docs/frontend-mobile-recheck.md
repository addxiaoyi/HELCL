# Mobile frontend recheck, 2026-11-19

This increment starts from `4aec474` and repairs five omissions found when the
original 100-item audit was checked again. It is implemented in the isolated
`frontend-recheck-mobile` worktree. It does not change shared `Mods.cs`.

## Repaired boundaries

- **Pagination (41):** deduplication uses source plus nonempty project ID, falling
  back to the legacy slug. Two pages containing four distinct Modrinth slugs and
  empty IDs now retain all four projects. The shared parser's project-ID fix is
  a separate parent change; this fallback remains necessary for legacy rows.
- **Search and file target (39–43):** searches capture instance, reference version,
  loader, resource kind, provider, query and order before scheduling work. Editing
  filters/query invalidates selection and releases while keeping the last list
  visible. Both the selected-row and inline-row actions reject stale bindings.
  File requests capture their project and inspected target, reject responses
  after a project/query/instance change, and cannot re-enable stale downloads.
  Download progress stays associated with its original detail view.
- **Back navigation (47, 65):** the return target is peeked while dirty-form
  confirmation is pending. Only successful navigation pops it; cancellation
  preserves both the draft and original return target. A pending-back guard
  prevents overlapping navigation.
- **Instance file actions:** cover import and ZIP export capture the instance,
  full source folder, instance revision and navigation revision before native
  file picking. They recheck after native awaits and before mutation. Switching
  away and back also invalidates the callback. Cancellation preserves the cover
  and reaches the cancelled task state. Export creates a temporary ZIP before
  opening the destination, so archive-generation failure cannot truncate a
  previously saved backup; temporary files are removed in `finally`.
- **Touch targets (62):** the primary-button override and tablet navigation rail
  now both have a minimum height of 44 logical pixels.

## Failure-first evidence

The first controlled 390×844 preview ran against the original behavior, with
only awaitable test seams added. It exited **1**, reporting all eight failures:
legacy pagination collapse; stale search selection; late release response;
cancelled-back target loss; subsequent-back target skipping; cover picker writing
to a newly selected instance; export picker overwriting after target change; and
primary/rail targets below 44 logical pixels.

Receipts are under `TestResults/mobile-recheck/red-390x844/`:
`mobile-recheck-preview.json` and `mobile-smoke-error.txt`. The corresponding
command output is `TestResults/mobile-recheck/red.log`.

## Final verification

- `dotnet build tools/HelL.Mobile.Preview/HelL.Mobile.Preview.csproj -c Release -v quiet`
  exited **0**, **0 warnings / 0 errors** on the final incremental source.
  Output: `TestResults/mobile-recheck/build-final.log`.
- `dotnet test tests/HelL.Mobile.Tests/HelL.Mobile.Tests.csproj -c Release`
  exited **0**, **57 passed / 0 failed / 0 skipped**. TRX:
  `TestResults/mobile-recheck/tests/mobile-recheck.trx`.
- The final desktop-hosted full UI smoke at **320×720**, **390×844** and
  **844×390** each exited **0**. Each contains passing `mobile-smoke.json`,
  `mobile-recheck-preview.json` and `mobile-frontend-remediation.json` in its
  `TestResults/mobile-recheck/final-<size>/` directory.
- The expanded controlled preview covers legacy slug pagination; stale provider
  selection and inline action; query editing; a late search after instance
  switch; late releases after project, instance and query changes; cancelled and
  confirmed back; cover callbacks after target/page changes; cancellation;
  existing export destination preservation; successful valid ZIP export; and
  minimum logical target height. The cover checks compare the original bytes,
  and export checks preserve a preexisting sentinel before a separate successful
  archive export.
- Remote/diagnostic **390×844** smoke exited **0**, preserving fingerprint,
  device-scoped revocation, stable rows, loading/cancellation, terminal-state,
  polling-on-leave and diagnostic assertions. Receipt:
  `TestResults/mobile-recheck/final-remote-390x844/mobile-remote-preview.json`.
- `git diff --check` passed. The 320px resource-filter screenshot was inspected
  for clipping after increasing the button height.

These are Windows-hosted Avalonia previews, not native Android acceptance.
Physical touch accuracy and TalkBack are **unverified**. The earlier parent
Android emulator receipts remain separate, and the parent will run the integrated
changes through native CI. A destination supplied by an arbitrary Android
document provider cannot promise transactional overwrite if the provider fails
after writing begins; this increment guarantees that an already stale picker
callback is rejected before opening the destination and stages archive creation
before replacement.
