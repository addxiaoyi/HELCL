# WPF frontend remediation — items 1–35

Scope: the complete GUI foundation at `4313450`, isolated in `D:/qwq/qdq/.tmp/frontend-wpf`.
No commit, push or original-workspace write was performed by this subtask.

## Item disposition

1. Implemented: `SecretInput` defaults to a real `PasswordBox`, offers explicit reveal/hide, and hides again when unloaded. Existing stored-key hints remain available without returning the secret.
2. Implemented: skin-site login uses `Dlg.EditAsync`; API/user/password checks and normalized-address errors stay in the open form with entered values retained.
3. Implemented: unified-pass login validates server ID/user/password in the open form before starting its task.
4. Implemented: device-code copy/browser controls start disabled, then become available only when the actual code/HTTPS address arrives.
5. Implemented: shared authentication events carry optional `expires_in`; WPF counts down the real TTL, disables expired actions and offers a fresh authorization. Missing TTL is explicitly unknown rather than fabricated.
6. Implemented: complete UUID is wrapped and can be copied from the account row.
7. Implemented: account name/UUID text search and type filter.
8. Implemented: the account hero/list stack at narrow width; account title/actions use wrapping responsive rows. No claim of every display/scale combination.
9. Implemented: favorites use acknowledged RPC; read failure is an explicit retryable error, not an empty collection.
10. Implemented: mod toggles await their commit, freeze duplicate input, roll back on failure, and report the row error; state text changes only after acknowledgement.
11. Implemented: resource refresh retains visible rows with an updating/stale notice; a failed update keeps the previous result available.
12. Implemented: a timed-out search offers an explicit 45-second retry/wait with the same criteria. Normal default remains nine seconds.
13. Implemented: the version filter is refreshed from the manifest's release list, while preserving editable input and the existing fallback choices if unavailable.
14. Implemented: recognized clipboard links require an explicit use/ignore decision before changing the search input.
15. Implemented: provider completion/failure metadata is preserved through `SearchCatalog` and displayed; the existing source selector supports retrying one provider. UI does not invent source status.
16. Implemented for all actual removable catalog files (mod/resourcepack/shader/datapack): move to the instance recycle store and offer undo; root's receipt/hash/no-overwrite checks protect restoration. The world catalog has no installed/delete method, so it is not a permanently deleting path; modpack label removal retains its separate semantics and keeps game data.
17. Implemented: recent metadata request scales to the full instance list (shared backend cap raised to 10,000 by root).
18. Implemented: sort and pinned-only preference persist alongside group/view preferences, with old preference files remaining compatible.
19. Implemented with bounded progressive rendering: initially 60 instance cards, explicit loading of further pages. This avoids constructing every card on initial load; it is deliberately described as paging, not claimed to be recycling virtualization.
20. Implemented: opening the launch page selects its target without modifying the global default instance.
21. Implemented: batch pin/unpin shows the exact target names/count and the number hidden from the current page before committing.
22. Implemented: default keyboard focus visual restored; existing arrow/Enter navigation remains intact. Screen-reader certification is not claimed.
23. Implemented: double-click selects/focuses the install confirmation rather than submitting installation.
24. Implemented: an active installation locks only its mutable target/configuration; version browsing, text search and category filtering remain available.
25. Implemented: version-list height follows available viewport height within bounded limits.
26. Implemented: explicit pre-install confirmation shows `InstallationPlan` destination, known client/library download estimate, available disk space and existing-file policy. Missing estimates are labeled unknown and require an explicit continue; known insufficient space refuses submission. Assets/Java/loader exclusions remain explicit.
27. Implemented: loader options start expanded and disabled options explain their compatibility/loading/active-task reason.
28. Implemented as transparent staged behavior: all numeric input validates before any directory mutation; if directory change succeeds but main settings save fails, the error explicitly identifies partial application. This is not an atomic transaction claim.
29. Implemented: resolution and download-rate values reject invalid/out-of-range input at the field; correction clears its error decoration.
30. Implemented: context-window input requires a value in the documented range, without silent clamping/defaulting.
31. Implemented: the visible footer distinguishes immediate preview/save/restart behavior and counts pending changes; leaving with pending edits warns that the draft remains unsaved.
32. Implemented: settings search returns category-linked text matches that switch the category and scroll to the matching field.
33. Implemented: conversation search matches titles and message content.
34. Implemented: chat deletion requires a specific confirmation and acknowledged RPC; delete errors are not silently converted to old state.
35. Implemented: AI history is an actual recycling virtual `ListBox`, with rows built by the existing virtual-row factory rather than thousands of eagerly constructed buttons.

## Verification

- `dotnet build src/HelL/HelL.csproj -c Release --no-incremental -v quiet`: passed, 0 warnings / 0 errors.
- `dotnet build tools/HelL.UiPreview/HelL.UiPreview.csproj -c Release --no-incremental -v quiet`: passed, 0 warnings / 0 errors.
- `dotnet test tests/HelL.Tests/HelL.Tests.csproj -c Release`: existing **816/816** passed; receipt `TestResults/wpf-remediation/wpf-full.trx`.
- New device-code TTL/legacy event tests: **2/2** passed; receipt `device-code-expiry.trx`. They verify the real `expires_in` event and preserve existing two-argument callers.
- `HelL.UiPreview.exe --desktop-gallery <folder>`: **20 settings panes / 44 field bounds checks** passed, including light/dark and 900/1180 widths.
- `HelL.UiPreview.exe --frontend-remediation <folder>`: **17 behavioral checks** cover real password masking/reveal, invalid settings refusing a commit, visible pending state/search, invalid account form retention, real TTL expiry, failed mod-toggle rollback, 125-row library rendering 60 then 120 cards, message-body search, 2,000-chat virtual containers, destructive confirmation and responsive list height.
- The behavior run exposed and corrected an actual account hero/list parenting error before acceptance. Controlled fixture issues (detached renew-button `IsVisible`, inherited `Render` reflection overload) were corrected without reducing the intended assertions.
- Existing `--workspace-views` navigation assertions passed after reacquiring the current nav control following asynchronous nav rebuild; its later artwork stage is blocked by its required historical screenshot fixture, absent from the isolated source checkout. The screenshot assertion was not removed or weakened.

The original screenshot review was a candidate audit. This subtask does not claim each UI condition has been independently exercised on every Windows version, high-DPI configuration or assistive technology. Whole-project combined tests and parent-branch integration remain the parent agent's final gate.
