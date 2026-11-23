# Desktop conversation and task-log typography

Baseline: `5f9af75f55f35086e02b46be5f772547d6428d91`.

## Changes

- Assistant prose is 14px with a 22px line height. Heading levels have a smaller, consistent hierarchy and blank Markdown lines become paragraph spacing rather than full-height empty controls. The model subtitle sits below the page title; secondary configuration actions no longer compete with it.
- Code blocks use 13px text on a 20px baseline, a quiet inset background, and a compact language/copy header. Long commands retain local horizontal scrolling. The renderer keeps code whitespace and unfinished fences; copying still uses the original captured code text, including indentation and empty lines.
- Code font candidates select a real system monospace face. An application font fallback restricted to CJK and full-width ranges uses the existing embedded Noto Sans SC. Latin stays monospace; Chinese runs are checked through actual shaped glyphs rather than a family-name string. No new font files or dependency are introduced.
- Task rows and logs have clearer text sizes and line heights. The sparse list no longer leaves a full-height blank card before the log: a single row is followed by the detail panel with a 14px gap. Many rows remain in a viewport bounded by the available page height. The full task title and its action row are separate, and expanded logs keep a scrollable viewport in a short window.

The initial typography pass did not change AI requests or task behavior. Subsequent fixes also cover installation confirmation/cancellation, task history/retries and resource refresh scheduling; see the dated follow-up in [the reading record](frontend-reading-2026-11-20.md). The previous 66 vector drawings and all recovery checks remain required.

## Evidence

`MainWindow.ReadingCheck.cs` runs in the existing actual `--ui-smoke` chain. Its controlled messages include mixed Chinese/Latin prose, a long command, a blank code line and four-space indentation. It captures the same data in dark/light themes at 800px and 1180px, then checks a 20-row list in an 800×600 window, expanded log visibility and an unfinished code fence. It restores messages, task selection, page, theme, window size and the fixture count afterward; it does not send a real AI request or add the controlled task to persisted history.

The original same-fixture render uses 13px prose, 12px code with roughly 16px automatic line height and an 11.5/18px task log. The explicit `monospace` string resolved to Segoe UI for Latin on this Windows host; Chinese fell back to Microsoft YaHei UI. The first valid before receipt recorded 16 typography/action-placement failures. Later comparisons also require an actual Latin monospace face, embedded CJK fallback and sparse-list row-to-detail spacing.

The final same-helper source comparison records **28 failed conditions** before the repair and **53 passing conditions** afterward. Code glyph runs contain **152 glyphs, zero missing glyph IDs**; Latin resolves to **Cascadia Mono**, Chinese to **Noto Sans SC**. Prose, code and log text have actual **Normal** weight. The single visible row-to-detail gap changes from **270px to 14px** in both themes and widths. The title gets about **503px** at 800px window width and **725px** at 1180px, while copy/expand controls have their own row.

```powershell
dotnet build src/HelL.Desktop -c Release --no-incremental -v quiet
dotnet src/HelL.Desktop/bin/Release/net10.0/HelL.Desktop.dll --home <new-isolated-profile> --ui-smoke
dotnet test tests/HelL.Desktop.Tests -c Release --logger 'trx;LogFileName=desktop.trx'
git diff --check
```

The full nonincremental build exits 0, retaining the baseline Core's 16 platform/obsolete-API warnings. The final incremental build is 0 warnings/0 errors. Desktop service tests pass **27**, with **1 Unix-only permission check skipped on Windows**. Existing UI smoke, recovery, document-release binding, detail, icon and refinement receipts remain successful. New drawing/text samples and the old action tests run together.

Evidence is under `TestResults/frontend-reading-20261120/desktop/`: `before-final/`, `before-complete/`, `before-delivery/`, `delivery/reading/`, `tests/desktop.trx`, logs and source-restoration receipts. The first two exploratory helper attempts failed to serialize the baseline's automatic (NaN) line height; these attempts are retained separately and are not claimed as valid comparison receipts. The final before/after source comparison temporarily restores only the five owned product files, then restores their exact bytes in `finally`; no Core code or assertions are weakened. Generated profiles, images and temporary keys are excluded from the handoff.

The Windows screenshots were inspected for paragraph rhythm, code/header spacing, task density and long log wrapping. Linux glyph selection and screenshots remain an integrated-CI gate; Windows font results do not certify Linux/macOS or native screen-reader output.

## Borderless transparency follow-up (2026-11-23)

The desktop window now removes native decorations, the outer stroke, card outlines, sidebar/footer separators and decorative shadows. Grouping uses translucent fills. Keyboard focus and invalid-input indicators remain visible. Invisible 5px edge/corner targets call Avalonia's resize operation and are hidden while maximized; title dragging and window controls retain their existing handlers.

The standard window tint changes from 58% to 40% opacity in dark mode and from 68% to 45% in light mode. Large panel fills change from 87% to 44% dark and from 92% to 25% light. Blur is preferred over the heavier acrylic material; plain transparency preserves its alpha, while unsupported transparency and the explicit solid option remain opaque. These overrides are scoped to Desktop.

The Windows preview was inspected before and after removing strokes. The full UI smoke passes at `TestResults/borderless-ui-smoke-20261123/`; the existing no-compositor readability checks remain intact and report contrast above 9:1 in both themes. Desktop service tests pass 29 with one Unix-only skip, at `TestResults/borderless-tests-20261123/desktop.trx`. Final manual edge-drag verification was interrupted by concurrent user input, so it is not counted as a passed native interaction check.

## Settings, status and launch-entry recovery (2026-11-23)

Unsaved download-source and thread-count edits now survive background-task completion, failure and navigation. The page shows the pending draft and an explicit discard action. Valid saves use the existing atomic configuration commit; validation or write failures retain the draft, explain the error inline and preserve the last saved in-memory settings.

Task badges use theme resources instead of fixed model brushes. Failure and neutral states have opaque small backplates so their text contrast does not depend on wallpaper. Rendered selected-row checks cover running, cancelling, completed, failed and cancelled states while switching the same task between dark and light themes: all ten samples exceed 6.8:1. State changes are exercised after the row is rendered.

Instances with no installed version show one primary “安装游戏版本” action, opening the installer for that instance. Existing versions retain launch and additional-install actions; clearing a version selection does not incorrectly offer first installation. The instance heading is keyboard focusable and opens a selectable full-name flyout; both heading and sidebar expose full-name hover hints.

`MainWindow.SettingsDraftCheck.cs`, `MainWindow.TaskBadgeCheck.cs` and `MainWindow.LaunchEntryCheck.cs` run in the complete Desktop UI smoke. Failing-first receipts are under `TestResults/ux-fixes-red-20261123/`; final passing receipts, ten badge samples, first-install and full-name images are under `TestResults/ux-fixes-final-20261123/`. Desktop service tests pass 29 with one Unix-only skip at `TestResults/ux-fixes-tests-20261123/desktop.trx`. The controlled install-entry check launches no game and downloads no files.

The pending native-window check was completed on Windows at 150% display scale. All four edges and four corners received their matching resize events and changed bounds. Title dragging changed position without changing size. Mouse maximize hid the resize targets; restore returned to the exact prior size and position and re-enabled them. Raw events and the checked summary are at `TestResults/native-window-20261123/native-window-events.json` and `native-window-receipt.json`. Earlier failed tool-coordinate attempts are excluded. This confirms this Windows host, not other operating-system window managers.

Product commit `871fb06` passed [standard CI](https://github.com/addxiaoyi/HELCL/actions/runs/45169826816) and [frontend acceptance](https://github.com/addxiaoyi/HELCL/actions/runs/45169826852), including the new Linux UI receipts, all three portable platforms, WPF and native Android acceptance. The independent verifier also passed the complete Desktop smoke and service tests. The PR remains an unmerged draft.
