# WPF frontend recheck: virtualization and draft preservation

Baseline: `4aec4742068479a84521747c51a2db3d40ba383e`.

## Confirmed gaps

- Audit #19 used cumulative batches of 60 cards. Loading further batches retained every prior card, so large libraries still accumulated controls.
- Audit #31 allowed `ui_changed` refreshes to replace edited settings and clear their unsaved-change state.
- Choosing a game directory immediately changed backend state and reloaded the form before Save, discarding unrelated edits.

The failure-first actual WPF run recorded failed assertions for draft content, dirty state, navigation back to the draft, and bounded realization after scrolling. The old implementation reached 2,000 cards; that run was stopped during its subsequent view rebuild. Red evidence is in `TestResults/frontend-recheck-wpf-red.log`, outside the implementation checkout. The regression's legacy fallback now stops after two further batches to reproduce the same failure without creating all 2,000 controls.

## Changes

- `InstanceTiles` now groups lightweight instance records into responsive rows and uses a recycling, pixel-scrolling WPF virtualizing list. It receives a bounded viewport directly; an outer ScrollViewer no longer causes eager measurement of the full library.
- Card references and their picture lookups follow Loaded/Unloaded lifetime. Scrolling releases old card references; the full filtered record list remains available for search, sorting, selection, and batch operations.
- Keyboard movement traverses the complete filtered list and scrolls its target into view. “Select all current results” includes records outside the realized viewport.
- Settings refresh defers when a draft exists and explains the pending backend change. During a clean reload, input is briefly disabled and restored in `finally`, including failures. Theme refresh and dirty-state clearing stay within the same protected reload.
- The directory chooser only fills the draft text field. Directory, language, multi-instance, appearance and general settings now use one validated bridge RPC and one atomic configuration replacement. A failed file promotion preserves both memory and prior disk bytes; lost acknowledgement is reported as unknown, with the draft retained.
- Appearance persistence uses the backend bridge, including external bridge mode; GUI code never commits a stale full configuration snapshot. Existing writable linked directories remain supported.
- Pending saves lock the form and reject duplicate submission. Secret-input clearing after acknowledgement does not create a new draft. Explicit theme loading confirms draft replacement; selecting a theme alone is not a settings change.

## Verification

All commands ran in the isolated `frontend-recheck-wpf` checkout on Windows with separate generated preview profiles.

```powershell
dotnet build tools/HelL.UiPreview -c Release --no-incremental -v quiet
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-recheck <output-folder>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-remediation <output-folder>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --verify
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --workspace-views <output-folder>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --workspace-refinement <output-folder>
git diff --check
```

Build: exit 0, 0 warnings, 0 errors. Targeted recheck: exit 0, 17 passing assertions against 2,000 records; final narrow viewport retained 6 cards. It checks repeated scrolling, direct access to the final record, compact view, keyboard movement across unrealized records, all-result selection, narrow bounds, dirty-state retention, and leaving/returning to settings. Existing remediation regression passed. Baseline WPF checks passed 153/153; workspace view checks passed 143/143; workspace refinement checks passed 35/35. Diff whitespace check passed.

The broader view checks use the recorded Minecraft screenshot at `.design/resource-polish-2026-11-10/resource-pack-enabled.png`; the original fixture was copied into this isolated checkout for these runs. Screenshots and JSON receipts are under `TestResults/frontend-recheck-wpf/` in the parent workspace. Native screen-reader behavior has not been certified by these desktop-hosted checks.

## Integrated settings regression

`SettingsCommitTests` records failed promotion, combined-field persistence, unwritable targets and Unix symlink compatibility. The initial three settings regressions failed before the implementation change, then passed. `FrontendRecheckCheck` additionally reproduced five real UI/RPC failures for pending input, duplicate save and snake-case appearance serialization before the final repair. The final run passes the real save, canonical appearance persistence, acknowledged key clearing, failure draft retention and unchanged bytes checks. `SettingsThemeRecheck` adds 13 actual UI/RPC assertions for explicit theme loading, cancellation, confirmed draft replacement and backend/form agreement. Receipts are under the parent workspace's `TestResults/frontend-recheck-20261119/`; generated profiles are intentionally excluded from Git.
