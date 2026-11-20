# Mobile visual refinement

Baseline: `e8403e1f1f058bb27927d2fa0628f889cbebd812`.

The mobile client keeps its black/white primary actions, bundled Chinese font,
shared 66 vector icons, navigation, resource compatibility rules and existing
recovery behavior. This increment refines three related surfaces rather than
introducing another visual style.

## Surface and type hierarchy

Grouped panels use mobile-local opaque surfaces and fine dividers instead of
the shared desktop glass gradient. Dark groups are `#181C23`; light groups are
white. Selected rows and segmented controls use quieter local fills. The shared
design tokens and the desktop client are unchanged. Existing buttons retain
their 44 logical-pixel minimum target.

The current instance value has more emphasis than its field label. Page headings
are explicitly 22px, so the existing font-snapshot pass preserves the intended
title/caption hierarchy rather than recording their initial inherited body size.
Resource titles are 15px with a separate 12px summary. Acquisition uses a smaller
outlined secondary action instead of another filled pill. Normal-size rows stay
compact.

Long instance names previously consumed the automatic value column and squeezed
the field label away. Two actual light/dark checks failed first; the value now
uses the flexible column with ellipsis, while its label remains visible. Large
phone text continues to use the existing stacked home fields.

## Dynamic resource text and narrow reflow

The 200% review exposed a real inconsistency: results created after the initial
font pass remained at 15px/13px while headings and captions were 44px/24px.
The new actual bounds/typography check failed in both themes before the fix.
Resource item creation, source changes and returning to the page now queue one
layout-aware pass. It includes the list's visual templates, reuses the weak
font snapshots and the existing restore-before-sampling algorithm, checks
disposal and removes event subscriptions at disposal.

At 200%, title/action text now renders at 30px/26px. A narrow phone moves the
acquisition action below the title and summary, allowing two text lines rather
than squeezing the title between the image and enlarged action. At 320px the
long-title reading width is about 205px and the action is about 79 by 51px. Wider
layouts restore the inline action without reducing the text scale; returning
narrow restores the stacked layout.

The refinement helper preserves fixture state, results, theme, navigation
history and reading offsets, then lets its queued typography work settle before
restoring the final offsets. It does not perform network resource searches.

## Verification

`CheckMobileRefinementAsync` is called by the existing full mobile smoke chain.
The receipt `mobile-refinement.json` records rendered surface samples, text
contrast, actual touch bounds, title hierarchy, long-name bounds, dynamic text
scale, repeated refresh stability and hidden-to-visible creation. Desktop-hosted
200% runs add width round-trip checks using a real Window; Android has no Window
and does not claim those resize assertions.

Dark normal-text contrast on the sampled group is approximately 16.33:1 for the
instance value and 7.53:1 for its label; light values are 17.85:1 and 5.90:1.
Primary action contrast is 18.06:1 dark and 17.85:1 light. Top/bottom interior
pixels are opaque and identical, confirming the group is no longer a vertical
gradient. These checks cover the changed surfaces, not every application color.

```powershell
dotnet build tools/HelL.Mobile.Preview -c Release --no-incremental -v quiet
dotnet test tests/HelL.Mobile.Tests -c Release --logger 'trx;LogFileName=mobile-final-source.trx'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
$env:HELL_MOBILE_SYSTEM_TEXT_SCALE = '2'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
```

Complete previews at normal 320x720/390x844, 200% at both sizes, and remote
390x844 pass. The new receipt contains 36 checks in normal runs and 40 in
desktop-hosted 200% runs; all earlier smoke, icon, draft, focus, pagination,
reading-offset and system-font receipts remain successful. Mobile service tests
pass 57/57. The full rebuild retains the 16 pre-existing Core platform/obsolete
API warnings with zero errors; the final incremental preview build has zero
warnings and errors. No warning policy or business assertions were changed.

Evidence is under `TestResults/frontend-refine-20261120/mobile/`. The unmodified
`before-390x844/` screenshots use the same launch/resource fixtures as the final
`verified-*` screenshots. `dynamic-red-2x320/` and `long-name-red-320/` retain the
failure-first receipts; intermediate failed fixture/layout attempts are kept
separate from the final evidence. Final dark/light resource rows, home screens,
long names and wide/narrow 200% views were visually inspected. Generated
profiles, screenshots and temporary test keys are excluded from the source handoff.

These are Windows-hosted Avalonia previews and service tests. The parent branch
must run the integrated native Android pipeline. Physical touch accuracy,
TalkBack and vendor-specific keyboard/cutout behavior are not certified here.

The integrated Linux screenshot exposed one final alignment mismatch after the
column refinement: the account value stayed at the left of its flexible column.
Same-data bounds checks failed in both themes before correction. The account
value now shares the instance value's trailing edge in a normal single-row
layout and its leading edge in the large-text stacked layout. These two extra
checks bring the desktop-hosted normal/200% receipts to 38/42 assertions.
