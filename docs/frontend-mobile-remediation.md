# Mobile frontend remediation — 2026-11-19

Implementation is isolated in `codex/frontend-mobile`, starting at `4313450`.
No commit/push was performed and the original workspace was not changed.
The shared catalog paging/version changes were supplied by the parent agent and
are excluded from this mobile file handoff.

## Acceptance by audit item

| Audit | Implementation | Verification / remaining boundary |
|---|---|---|
| 36 | Rename the misleading import entry to “查找整合包”; it continues to open online modpack search. | Existing modpack-entry smoke assertion retained. |
| 37 | Bell opens a local task centre, showing current and up to 100 recent operations, target, progress and outcome; active operations can be cancelled. | Workflow unit tests and actual preview. |
| 38 | Search icon always reveals and focuses keyword input; filter icon independently expands/collapses filters. | Existing focus assertion and added preview. |
| 39 | Compatibility target is outside the collapsed filter area and remains visible above the list. | Actual preview assertion. |
| 40 | New searches retain the last successful list; failure/cancellation shows a retry message without erasing it. Filter changes mark results stale and prevent wrong-target selection. | Code path and combined preview. |
| 41 | “加载更多兼容资源” uses upstream offset/pageSize; offsets advance by requested page size and distinct source/project IDs append. Nonempty results allow trying another batch; only an empty upstream page marks the end, avoiding alias/dedup truncation. | Uses parent's tested shared paging API; UI compiles and smoke runs. |
| 42 | Resource-list offset is saved before entering details and restored on return. | Implementation retains independent offset; preview details flow passes. |
| 43 | File summary identifies the recommendation basis, title, date, compatibility and filename before download. | Actual detail screenshots. |
| 44 | Resource progress includes project/file and completed/total where available; local task centre retains terminal result. | Operation-history tests and task-centre screenshot. |
| 45 | Resource operations no longer disable page or bottom navigation. Conflicting actions report the active task instead of silently returning. | Controlled operation held while navigation/task centre remain enabled. |
| 46 | Name/version errors appear next to their form and focus the invalid input. Names reject path/control characters. | Existing invalid-create smoke plus form implementation. |
| 47 | Instance edits have a saved-state comparison and target-specific discard dialog; cancel retains the draft. | Actual dirty-navigation preview assertion. |
| 48 | Create/edit version fields retain manual input plus a bounded, cancellable official release picker; modern `26.x` IDs are accepted. | Release picker uses existing `RemoteVersions`, compile and manual-field smoke. Network picker remains dependent on upstream availability. |
| 49 | Instance form explains that reference version/loader only filter resources and do not install a game/loader kernel. | Visible explanatory field and preview. |
| 50 | Device-code panel has a one-second real countdown and explicit expired/restart wording; cancellation stops it. | Existing device-auth replay and new timer lifecycle. |
| 51 | AI follows only when the user is near the bottom, otherwise exposes “回到最新消息”. | Scroll-state implementation and replay. |
| 52 | Failed AI prompt returns to draft and offers retry/edit actions. | Existing replay plus retained failed prompt. |
| 53 | Clear chat requires explicit confirmation and supports undo. | Existing smoke updated to explicitly accept confirmation; history undo unit test. |
| 54 | Chat renderer separates fenced code, headings and lists; code has clipboard action and HTTPS links have explicit browser buttons. | Compiled renderer and chat-layout/replay screenshots; native clipboard/browser still need device interaction. |
| 55 | Bounded local `mobile-chat.json` persists at most 48 entries with a clear on-device storage notice; interrupted replies restore as stopped. | Roundtrip, invalid-record and undo tests; no API key is serialized. |
| 56 | Pairing adds bounded JSON file import and public-offer file export, retaining explicit fingerprint confirmation and existing validation. | Build and existing pairing/revocation suite. Native document picker needs device verification. |
| 57 | Revoke prompts with the exact target name and fingerprint, replacing the list-global checkbox as the active confirmation. | UI implementation; direct controller revoke tests retained. |
| 58 | Selected computer shows room-ticket and renewal-subscription expiry times separately. | Remote preview screenshot and report. |
| 59 | Remote host/task rows are keyed by stable IDs, updated in place and only inserted/removed when identity/order changes. | Actual remote preview asserts the same control references survive unchanged snapshots. TalkBack remains native-only. |
| 60 | Diagnostic fields use Chinese labels, with technical keys retained only in the data model. | Diagnostic preview/selection/export lifecycle passed. |
| 61 | JSON is a collapsible technical section, with larger flexible bounds; field selection stays primary. | Existing diagnostic lifecycle assertions retained. |
| 62 | Main icon, tab, inline, ComboBox and text-entry controls use 44 logical-pixel targets; explicit 32-pixel row buttons were enlarged. | Layout builds and three desktop-hosted sizes; physical hit-testing remains device acceptance. |
| 63 | Resource-row action accessible name includes its bound project title. | Compiled XAML binding; actual TalkBack needs device testing. |
| 64 | Android system FontScale is read and combined with a saved app 100/125/150/200% preference; text sizes are enlarged and labels wrap. | 1.5x preview captured; 2x system-font emulator/device run remains parent acceptance. |
| 65 | TopLevel.BackRequested and Escape feed a page stack, resource-detail back and confirmation dismissal. | Actual application RequestBack preview; Android IME-before-page ordering remains device acceptance. |
| 66 | InsetsManager safe-area changes update Android padding, with duplicate automatic padding disabled. | Hook compilation and actual-event report; cutout/gesture emulator/device evidence remains parent acceptance. |
| 67 | Android requests AdjustResize; InputPane events hide phone navigation while keyboard is open and reveal focused input. | Android ARM64 publish; actual keyboard resize remains parent acceptance. |

## Commands and results

- `dotnet test tests/HelL.Mobile.Tests/HelL.Mobile.Tests.csproj -c Release`:
  Initial **55/55 passed**, including five new task/history tests. TRX:
  `TestResults/frontend-mobile/mobile-remediation.trx`.
- `dotnet build tools/HelL.Mobile.Preview/HelL.Mobile.Preview.csproj -c Release`:
  **0 warnings / 0 errors** on the final incremental source.
- Desktop-hosted smoke at **320×720**, **390×844**, **844×390**:
  all exited **0**; receipts include `mobile-smoke.json` and
  `mobile-frontend-remediation.json`.
- Remote/diagnostic **390×844** preview exited **0**, retaining the previous
  loading/cancel/permission/failure/terminal-state assertions and adding stable
  row references. `final-remote-390x844/mobile-remote-preview.json`.
- `dotnet publish src/HelL.Android/HelL.Android.csproj -c Release -r android-arm64`:
  succeeded after explicit `global::Android.Views` qualification; existing shared
  Core warnings remain. The parent will build the final integrated APK again.
- The first new preview caught a real focus failure when opening search from a
  resource detail; Search now returns to the list before focusing. Red receipt:
  `preview-390x844/mobile-smoke-error.txt`, green in the later preview folders.

## Android event evidence

The existing isolated activity smoke extras remain supported:
`--ez hell_ui_smoke true --es hell_smoke_session <UUIDN>`.
Only when running Android with an explicit `MobileRuntime.ReportRoot`,
`mobile-platform-events.json` records actual BackRequested/SafeAreaChanged/
InputPane events, counters, safe padding, keyboard state, system scale, page and
hook availability. It contains no account, input text, tickets or keys. An absent
event is not counted as a passed native test. The parent can drive adb after the
smoke finishes on Home and pull this report for emulator acceptance.

Desktop-hosted previews are not Android rendering, keyboard, TalkBack or physical
device evidence. JSON file import deliberately avoids adding a QR/scanner
dependency; it is the controlled-file alternative requested by the audit.

## Final safety review

The parent-requested review found and repaired three additional boundaries:

- Typography baselines now use `ConditionalWeakTable`; removed remote controls
  are collectible while surviving controls keep their original font baseline.
- Confirmation covers the entire page and blocks background page/chrome/bottom
  navigation; Back still cancels and restores prior enabled states. Chat clear
  rechecks operation/disposal after confirmation and reports a busy model safely.
- Unreadable chat history blocks later automatic saves. An explicit confirmation
  offers “备份损坏记录并重置”: the exact original is moved to a random corrupt backup
  (preserving its permissions) before a fresh history is permitted. Save failure
  does not erase that backup, and the user has an actionable recovery path.

Combined old-source UI acceptance failed with
`retainedControl=True, clearedDuringOperation=True, backgroundAvailable=True`.
The repaired full 390×844 smoke passed. A separate regression first proved that
old `Save` overwrote a rejected history; the repaired full Mobile suite is now
**57/57**, with **7/7** workflow tests verifying bytes and Unix-mode preservation,
explicit recovery and successful subsequent persistence. Final preview build
remains **0 warnings / 0 errors**.

Private pairing-file callbacks also capture owner, selected host and navigation
revision, and discard results after leaving/re-entering the page, host switching
or disposal. Stream reads and public-offer saves recheck visibility/disposal after
their native awaits. The remote preview verifies that an earlier page visit's
file context is rejected; existing fingerprint/invitation validation remains.
