# Mobile AI and task reading refinement

Baseline: `5f9af75f55f35086e02b46be5f772547d6428d91`.

This increment preserves the mobile routes, stored conversations, safety checks,
shared vector icons and existing home/resource layouts. It refines three reading
surfaces without introducing dependencies or animation.

## Changes

- AI prose has an explicit 15px/24px baseline, headings 17px/27px, and role labels
  12px. Empty completed-state notices no longer add unused spacing. Existing
  Markdown line/list/heading ordering remains intact; this is not a new full
  Markdown parser.
- Code blocks use a compact language/copy header, a 44px minimum copy target and
  a 13px/20px baseline. Long commands scroll inside their code surface rather
  than widening the message. Bundled Cousine Regular provides fixed-width Latin
  characters, with the existing embedded Noto Sans SC fallback for Chinese. Code content retains
  trailing whitespace and unclosed streaming fences; copying still receives
  the rendered block's original code string.
- Task history uses separated reading rows instead of one heavy card per
  entry. The title, state and target/time have distinct roles, empty messages
  are omitted, and the action remains visible at a complete touch size. State
  labels stay on one line. Right reading padding protects punctuation overhang
  beside the overlay scrollbar; the previous safe-viewport assertion remains.

The normal composer now says “仅存本机 · 最近 48 条 · 清空可撤销”. Its existing
expanded connection panel retains the full file/location, history limit and
no-key-storage explanation. Storage errors and corrupt-history warnings remain
full, actionable messages; clear/undo behavior is unchanged.

`ChatContent.TextScale` owns the document's explicit font-size and leading
baselines. MobileHome excludes document descendants from its global weak font
snapshot, and a coalesced message-only layout pass sizes newly generated AI
templates. Repeated scaling does not multiply either font or leading. The event
is removed on Dispose and queued work exits after disposal.

## Verification

The same isolated chat/code fixture was rendered before and after at 390px in
both themes. The first actual UI run failed six assertions: the normal body was
12px, equal to its role label, while the code was 12px with no explicit leading.
Those receipts remain in `mobile/red-390/`; the earlier NaN receipt-serialization
fixture error is retained separately in `before-390/`.

The final `mobile-reading.json` contains **32 actual checks** across both themes:
normal/doubled font and line-height, role/content hierarchy, 44px copy bounds,
code-byte preservation, local horizontal scrolling, real nonzero glyph indices,
equal Latin monospace advances, stable repeated sizing, copy visibility above
the composer, unclosed-fence whitespace, task-column containment and state layout.
Android's initial runs resolved Latin code to proportional Noto Sans SC even
with explicit Android system family candidates. The test now requires the
bundled **Cousine** family, nonzero glyphs, and equal `i` and `W` advances.
It also measures code viewport and extent and scrolls to the last column
without moving the outer conversation. The bundled font comes from the pinned
Google Fonts source listed in `THIRD_PARTY_NOTICES.md` and ships with its OFL.

The new reading check runs inside the existing complete smoke chain and keeps
all earlier draft, focus, resource-position, icons, refinement and 200% system
font checks. Screenshots suppress only the prior test's undo/status fixtures
and restore their original visibility/text in `finally`; ordinary product
clear/undo behavior is unchanged.

```powershell
dotnet build tools/HelL.Mobile.Preview -c Release --no-incremental -v quiet
dotnet test tests/HelL.Mobile.Tests -c Release --logger 'trx;LogFileName=mobile-reading.trx'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
$env:HELL_MOBILE_SYSTEM_TEXT_SCALE = '2'
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 320 720
dotnet tools/HelL.Mobile.Preview/bin/Release/net10.0/HelL.Mobile.Preview.dll <new-profile> 390 844
```

Final complete build: exit 0, zero errors and the existing Core's 16 platform/
obsolete-API warnings. The final incremental build has zero warnings/errors.
Mobile service tests: **57/57**. Evidence is under the parent workspace's
`TestResults/frontend-reading-20261120/mobile/`; generated profiles, fake keys,
screenshots and TRX are excluded from the source handoff.

Complete ordinary 320×720 and 390×844 previews, 200% 320×720 and 390×844
previews, and the 390×844 remote preview all exit 0. Each ordinary/system-font
run preserves the previous complete smoke receipts and passes the 32 new
reading checks. The final ordinary light/dark chat/task and 200% code screenshots
were visually inspected; long code uses its own horizontal viewport, and its
copy control can be brought above the fixed composer.

These are Windows-hosted Avalonia UI and service checks. Integrated native
Android must independently verify the normal and 200% reading receipts. Physical
touch, TalkBack, vendor keyboard/cutout behavior and every native font remain
unverified by this increment.
