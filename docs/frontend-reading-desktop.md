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
