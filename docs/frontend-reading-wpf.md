# WPF installation and task reading refinements

Baseline: `5f9af75f55f35086e02b46be5f772547d6428d91`.

This increment keeps the existing purple theme, shared vector drawings, page
navigation and installation behavior. Product edits are limited to the version
page, its existing list template, and task-card presentation.

## Changes

- The version list and confirmation panel have explicit reading order labels.
  The confirmation choices sit in one quieter surface; the selected version is
  21px with a 28px line height, while its explanatory text is 13px/20px. The primary
  installation button keeps a 44px target. No loader rules, installation arguments,
  target selection, disk estimation, double-click behavior or confirmation logic
  changed.
- Version identity and release dates are more legible: the existing version
  template uses 13.5px medium identity text and 12px muted dates rather than the
  prior 11px faint dates. The same bindings, selection and virtualization remain.
- Task cards separate outcome and start time from the title and error explanation.
  Long error paths still wrap in full; status text uses 13px/20px. Expanded logs
  retain selectable, read-only content and independent scrollbars, with a 220px
  reading area and 13px text on 20px lines. Completed tasks release the progress
  strip; running tasks keep progress. Cancellation, removal, retry and crash
  analysis actions retain their existing handlers.

## Evidence

`WpfReadingCheck` runs in the existing `--frontend-recheck` chain. It uses four
offline manifest rows, a long instance name and a single controlled failed task
with a long local-path error. It never clicks Install or invokes a remote
installation. The check removes its own task and restores the manifest,
instance list, theme and window size in `finally`. Toasts from earlier controlled
checks are cleared before the comparison screenshots.

The initial unmodified-product run recorded **28 failures** among 42 checks:
the original 40px install action, unseparated typography/order, 11px dates,
task error/log density and absent separate outcome marker. The same fixture
then passed after repair. Additional checks cover running-to-finished progress,
long identities, log-selection retention and real scroll access at 200% page
layout; the final receipt contains **49 passing checks**. At 200%, the check
scrolls the actual installation button and the log wrapper into the viewport
and verifies their final bounds. The scaled captures exercise WPF layout; they are not physical DPI or
screen-reader certification.

Actual snapshots cover 900px and 1180px windows in both themes, long version
identity, multiline error/path text and 200% page layout. The complete paths
remain available in the read-only task log. Before/after comparison rows use
the same controlled manifest and failure text, rather than current network data.

```powershell
dotnet build tools/HelL.UiPreview -c Release --no-incremental -v quiet
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-recheck <new-profile>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-remediation <new-profile>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --verify
dotnet build HelL.sln -c Release --no-incremental -v quiet
dotnet test tests/HelL.Tests -c Release --no-build --logger 'trx;LogFileName=wpf.trx'
git diff --check
```

The full WPF solution and preview builds exited **0**, with **0 warnings and
0 errors**. Full WPF tests passed **818/818** using a process-local short
`TEMP`/`TMP` (`D:/qwq/qdq/.tmp/reading-test-temp`), without changing Core or test
assertions. Existing frontend-remediation checks passed **17** and the base WPF
verifier passed **153**. All existing combined UI, icon and home-aesthetic
checks remain in the chain.

Evidence is retained under `TestResults/frontend-reading-20261120/wpf/`, including
`before/reading/`, `scroll/reading/` final reading screenshots and JSON, build logs and
`tests/wpf.trx`. Generated preview profiles, captures and fixture account/config
files are excluded from the source handoff. Final integrated GitHub and native
device results remain separate acceptance boundaries.
