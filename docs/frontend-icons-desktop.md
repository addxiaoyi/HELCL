# Desktop vector icons and readable labels

Baseline: `1d147f0090ca04f3c4cad3e9ab4b33bd64981125`. The existing product layout and brand mark remain in place.

## Changes

All desktop navigation, window controls, resource recommendations and row actions now use the shared `HelL.Design.LauncherIcon` drawings. The project links the common catalog and renderer; Material icon markup, stylesheet and package reference are removed. Navigation uses 18px drawings and keeps its selected stroke synchronized with the selected page. Settings and instance creation now have matching gear and plus drawings. The maximize/restore action updates both its drawing and accessible name; the target-instance title is vertically aligned with the window controls.

Resource, world, backup and server delete buttons have target-specific accessible names. Decorative drawings remain outside keyboard focus and hit testing; the button owns the action and its label. Existing hover and focus treatments continue to supply state colors through the inherited foreground.

The previous GitHub Linux desktop screenshot at `TestResults/frontend-details-20261120/github-final-head/frontend-preview-44644988095/desktop/ui-1180.png` showed Chinese labels as missing-glyph boxes. The desktop declared system font names but did not bundle the existing mobile Chinese typefaces. It now links those same three Noto Sans SC assets and uses their embedded family for text. No new font files or graphics dependency were added.

## Verification

`MainWindow.IconCheck.cs` runs in the existing actual `--ui-smoke` chain. It renders all 66 canonical drawings and every desktop alias at 14, 16, 20 and 24 pixels: **368 nonempty bitmap samples**. Canonical path bounds, inflated for the active stroke, stay inside the 24-unit frame. Changing the kind, selected stroke and foreground produces different rendered pixels. It also verifies selected navigation foreground inheritance and the absence of extra icon input targets, both themes at 800px and 1180px, and target-specific resource delete names. The atlas height follows the complete catalog so the final row remains visible.

The Chinese check inspects the resolved embedded `Noto Sans SC` family and eight real nonzero character-to-glyph IDs for `设置实例存档资源`; it does not equate a family-name string with readable glyphs. The recorded Windows screenshots show Chinese text and the shared drawings. Linux rendering must still be confirmed by the integrated CI receipt and screenshot; this subtask does not claim that remote result in advance.

```powershell
dotnet build src/HelL.Desktop -c Release --no-incremental -v quiet
dotnet src/HelL.Desktop/bin/Release/net10.0/HelL.Desktop.dll --home <new-isolated-profile> --ui-smoke
dotnet test tests/HelL.Desktop.Tests -c Release --logger 'trx;LogFileName=desktop.trx'
git diff --check
```

Final build exit **0**, with **0 errors**. The full nonincremental build retains 16 existing Core platform/obsolete-API warnings; none are introduced in the desktop increment. Actual UI exit **0**; `desktop-icons.json`, `ui-smoke.json`, `frontend-recovery.json`, `desktop-recheck.json`, `desktop-details.json` and `document-release-binding.json` are all successful. Desktop service tests: **27 passed, 1 Unix-only permission check skipped on Windows**. Diff whitespace check passed.

Evidence is under `TestResults/frontend-icons-20261120/desktop/`: `ui-delivery/desktop-icons.json`, `ui-delivery/icons/atlas-{light,dark}.png`, `desktop-{light,dark}-{800,1180}.png`, build and UI logs, and `desktop.trx`. The atlas and narrow light/dark views were visually inspected. Generated profiles and screenshots are not part of the source commit. Native screen-reader output and physical-device behavior remain separate checks.
