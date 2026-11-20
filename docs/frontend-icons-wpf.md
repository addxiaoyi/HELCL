# WPF vector icon redraw

Baseline: `1d147f0090ca04f3c4cad3e9ab4b33bd64981125`.

The launcher keeps its existing layout, brand mark and button actions. Navigation
and common operation glyphs now draw the shared `IconCatalog` geometry with a
24-unit frame, 1.8-unit rounded strokes and the current foreground brush.

## Integration

- `Ui.Glyph` retains its `TextBlock` return contract. `VectorGlyph` preserves
  legacy `Text` values and bindings, including the maximize/restore content
  change. WPF seals `TextBlock`'s renderer, so mapped symbols use a fixed-frame
  geometry drawing while a text effect suppresses the original font ink. Text,
  size and foreground metadata update that drawing. Geometry alone is cached;
  theme brushes remain live.
- Unmapped symbols retain the original text rendering. Existing specialized AI
  drawings such as the brain and thumb symbols remain available. The shared
  factory does not turn arbitrary text into an icon name, and this increment
  does not claim to remove every remaining legacy font template.
- Decorative factory glyphs do not intercept pointer input, receive keyboard
  focus or add a separate automation peer. Icon buttons retain their existing
  named action. This checks UI automation structure, not screen-reader speech.
- Main navigation and category rails use a 16px frame. The title-bar controls
  use matching 14px frames inside their existing 46x48 buttons; their focus and
  hover styles remain intact. Mod management uses the puzzle symbol rather than
  the layout/layers glyph.
- Common AI actions use the same shared paths for copying, deleting, confirming,
  closing, searching, downloading and related operations. The AI/Lucide path
  helpers use the same stroke thickness; specialized symbols keep their meaning.

## Render verification

`WpfIconCheck` runs through the existing `--frontend-recheck` entry point.
It rasterizes real controls and all 66 catalog paths, checks complete stroked
bounds, verifies dynamic symbol/color/size changes, preserves unknown-symbol
fallback, checks parent action names and decorative peers, and samples the actual
window button region for the maximize-to-restore change. It also checks all three
caption frame and ink centers. Sampling a live child never rearranges it.

Light/dark catalog PNGs show 14/20/28px icons. Actual launcher screenshots cover
1180px, 900px and 2x raster output. Early visual inspection rejected an atlas that
used long canonical names through the legacy text factory and exposed a helper
that rearranged the live caption; the final atlas uses framed paths, and caption
verification crops the rendered window. Those intermediate receipts remain
separate from the final run.

```powershell
dotnet build tools/HelL.UiPreview -c Release --no-incremental -v quiet
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-recheck <new-evidence-folder>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --frontend-remediation <new-evidence-folder>
tools/HelL.UiPreview/bin/Release/net48/HelL.UiPreview.exe --verify
dotnet build HelL.sln -c Release --no-incremental -v quiet
dotnet test tests/HelL.Tests -c Release --no-build
```

The actual icon run passes 151 assertions, including the 66 raster samples and
their frame bounds, alongside the existing 53 combined checks. Existing frontend
remediation passes 17 checks and the base WPF check passes 153. UI and complete
WPF builds pass with zero warnings and errors. The complete 818-test suite passes
with process-local `TEMP`/`TMP` set to the short isolated directory
`D:/qwq/qdq/.tmp/icon-test-temp`.

A preceding run using the deeper evidence-directory temporary root passed
817/818 and failed the phone-transfer fixture's file-existence assertion; its
isolated rerun failed at the same line. The earlier and short-root TRX receipts
are both preserved. No synchronization code or assertions were changed to obtain
the successful run. Integrated GitHub validation is a separate gate.

Evidence: `TestResults/frontend-icons-20261120/wpf/`, including the final
`ink-ui/icons/wpf-icon-check.json`, light/dark atlases, caption crops,
navigation PNGs, build logs and `tests/wpf-frozen.trx`. The final frozen-source
sequence rebuilt the whole WPF app and preview tool, then passed the combined
actual UI run and all 818 tests again. The final minimize-centering coordinate
was subsequently rebuilt and checked in the `ink-ui` run, including separate
frame and ink-center assertions; that final coordinate did not rerun the unit
suite locally. The parent CI checks the integrated source. Generated profiles and images are not
part of the source patch. Native screen-reader/high-contrast certification and
physical display scaling remain separate from these WPF raster checks.
