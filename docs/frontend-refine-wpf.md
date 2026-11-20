# WPF hierarchy and spacing refinement

Baseline: `e8403e1f1f058bb27927d2fa0628f889cbebd812`.

The visual pass preserves the existing pale/purple WPF theme, launcher actions,
brand and shared vector icons. It focuses on the launch workspace rather than
adding another page structure or decoration.

## Changes

- The empty launch card no longer reserves a column or a 36px spacer for the
  collapsed launch-options pane. `AdaptiveColumns` keys its arrangement on both
  children's `Visibility == Collapsed` states as well as width; either visible
  child fills the available width, and restoring both restores the original
  stacked or paired arrangement. `Hidden` and unmounted controls are not treated
  as collapsed. Existing two-visible-child behavior is preserved.
- An empty progress/status group collapses instead of reserving a second blank
  gap. Text and progress visibility independently bring it back. The launch-card
  title is a quieter 21px/28px beneath the page title; its supporting instructions
  use 13px text and a 20px line height. The primary button keeps its 44px target.
- News has clearer 12px metadata, 14px compact headlines, and 13px/20px summaries.
  Existing typography metrics supply caption/body line heights. Compact artwork
  is 112×80, related rows use a 16px rhythm, and summary height fits complete
  lines. Headlines, summaries, filtering and article-opening actions are retained.

## Visual and behavior evidence

`WpfAestheticCheck` runs in the existing `--frontend-recheck` chain. Its **34
checks** use real WPF measurement and arrangement, not a mirror of style strings.
They cover either collapsed child, restoring the pair at 400/900 widths, empty
status space, independent status/progress recovery, retained primary action size,
artwork row bounds, a genuinely wrapping long title, and a two-times page layout.

The baseline run with the same check recorded **five failures**: four hidden-pane
gap/width failures and one empty status-area failure. Those baseline sources were
temporarily replayed from the exact Git baseline, with all modified product bytes
backed up and restored in the isolated checkout. `before-clean/` is the comparison
set without the unrelated earlier account-check toast. `frozen/` is the final
source run; it passes all 34 checks plus the previous icon, field, draft and
library checks.

The screenshot comparison uses the same three explicit layout fixtures in both
themes at 900×740 and 1180×740, including a long headline and missing-cover state.
The check does not claim these fixtures are live official news or successful
external requests. Production news loading and translation are unchanged; a
separate restored-source screenshot is also saved. At 900px the fixture news
section begins at **y=334 instead of y=379**, releasing **45px** for useful content;
at 1180px it moves from y=343 to y=334. The install action remains 44px high.

The before/after light/dark views, long-title view and two-times page view were
visually inspected. The page-scale screenshot is a WPF layout check, not proof of
native display-DPI or screen-reader support.

```powershell
dotnet build HelL.sln -c Release --no-incremental -v quiet
dotnet build tools/HelL.UiPreview -c Release --no-incremental -v quiet
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-recheck <new-output>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-remediation <new-output>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --verify
dotnet test tests/HelL.Tests -c Release --no-build --logger 'trx;LogFileName=wpf.trx'
git diff --check
```

Final preview build: **exit 0, 0 warnings, 0 errors**. The full WPF application
build also exited 0 with no warnings/errors. Frozen actual UI passes the new
34-check refinement, existing **153 icon checks**, and previous combined checks;
the existing frontend remediation and base WPF verifier also pass.

A first local test command collided with a concurrent build output (`CS2012`);
the log is retained as `tests-build-collision.log`, and subsequent commands ran
sequentially. The full suite then passed **817/818**, with a wrapped-world import
fixture failing its existing directory-promotion operation with `Access denied`
under the isolated temporary directory. The unchanged two-case import test
passed **2/2** on the independent rerun. Both TRX receipts are retained. No Core,
world-installation code or assertions were modified. Final integrated GitHub
818-test validation remains a separate required gate.

Evidence: parent workspace `TestResults/frontend-refine-20261120/wpf/`, including
`before-clean/`, `frozen/`, `frozen-remediation/`, `wpf-aesthetics.json`, PNGs, build
logs and `tests/{wpf,world-import-retry}.trx`. Generated profiles and screenshot
fixtures are excluded from the source commit.
