# Desktop frontend recheck, 2026-11-19

Baseline: `4aec474`. Implementation and the evidence below were produced in the
isolated Windows worktree `D:\qwq\qdq\.tmp\frontend-recheck-desktop`, then copied
into the integrated frontend recheck. Evidence paths below are relative to that
isolated worktree; generated logs and test profiles are local artifacts.

## Four repaired gaps

- **Selected-file documents (93):** document requests now bind the project,
  target/filter, selected release ID and an independent request generation.
  Changing files immediately clears the previous document. Late success and
  failure cannot fill the new selection, including an A-to-B-to-A selection.
  When another task owns the task gate, the latest selection waits and loads
  after the gate becomes idle. Cancelling the current document exposes a local
  retry action rather than leaving an indefinite loading message.
- **Failed AI prompts (76):** successful sends and clearing the conversation
  clear the old failure and retry button. Retry preserves a different nonempty
  draft and explains how to proceed. A failed log prompt retains its requirement
  for upload review when retried; failure restoration does not overwrite a newer
  draft entered while the request was running.
- **Log privacy (74):** filtering covers Windows, UNC and POSIX paths, email,
  IPv4 and IPv6 in addition to the existing credential-line and URL filtering.
  A filtering timeout returns an omission message instead of the unfiltered
  content. Upload still requires preview and confirmation of the receiving
  service; filtering is not a guarantee that every possible identifier is gone.
- **Log viewer freshness (74–75 follow-up):** both successful reads and failures
  check the instance, selected file and read generation. A previous file, an
  older read of the same file, or a read invalidated by reload cannot overwrite
  the current text or disable its diagnosis button. Read errors show recovery
  guidance without placing the exception's private path in the viewer.

## Additional privacy boundaries

A read-only follow-up reproduced addresses ending in sentence punctuation and
partial path redaction when names contained spaces. The extension filters those
cases and IPv4-mapped IPv6 addresses. IPv6 is processed before IPv4 so a mixed
address is hidden as a whole. Sentence punctuation remains readable.

Quoted paths retain the diagnostic after the closing quote. For an unquoted
absolute path with an ambiguous ending, the filter conservatively hides the
rest of that line; this can omit a following diagnostic, but prevents a person's
name or directory suffix from surviving partial replacement. Multiple POSIX
components distinguish paths from ordinary single-component `/help` commands.

## Failure-first evidence

- `TestResults/desktop-recheck/log-identifiers-red.trx`: **7 failed / 1 passed**.
  The seven new UNC/POSIX/email/IPv4/IPv6 cases failed against the prior filter;
  the ordinary-message preservation case passed. Console output:
  `desktop-recheck-log-red.log`.
- `TestResults/desktop-recheck/ui-red/desktop-recheck.json`: `ok: false`, with
  `selectedReleaseDocument`, `failedRetryDraft` and `lateLogFailure` all false.
  The actual UI run failed with those three controlled regressions; its error
  receipt is `TestResults/desktop-recheck/ui-red/ui-smoke-error.txt`.
- `TestResults/desktop-recheck/desktop-redaction-boundaries-red.trx`: all **10**
  added punctuation/space/quoted-path cases failed before the extension. Console
  output: `desktop-recheck-redaction-boundaries-red.log`.

## Verification and its limits

The first complete service green run passed **17 tests**, with **1 Unix-only
permission test skipped** on Windows, **0 failures / 18 total**. Receipt:
`TestResults/desktop-recheck/desktop-recheck-green.trx`.

The desktop build and actual Windows UI smoke then ran successfully:

```powershell
dotnet build src/HelL.Desktop -c Release --nologo -v minimal
dotnet src/HelL.Desktop/bin/Release/net10.0/HelL.Desktop.dll --home <isolated-ui-final-home> --ui-smoke
```

The build exited **0**, with **0 warnings / 0 errors** on that incremental
build; output is `desktop-recheck-final-build.log`. The UI run exited **0** and
wrote passing `ui-smoke.json`, `frontend-recovery.json`, `desktop-recheck.json`
and `document-release-binding.json` under `TestResults/desktop-recheck/ui-final/`.

The document receipt records six controlled TCS checks: late reply rejection,
late failure rejection, latest-file loading after unrelated work, immediate
clearing on selection, a fresh request after reselecting the first file, and a
retry action after cancellation. The AI/log receipt checks preservation of a
new draft, clearing retry after success, rejection of old failures after file
switches, rejection of an older same-file read, and invalidation by reload.

After the privacy extension, the full service suite was recompiled and rerun:

```powershell
dotnet test tests/HelL.Desktop.Tests -c Release --logger 'trx;LogFileName=desktop-redaction-boundaries-green.trx' --results-directory TestResults/desktop-recheck --nologo -v minimal
git diff --check
```

Result: **27 passed / 0 failed / 1 Unix-only skip / 28 total**, exit **0**.
Receipt: `TestResults/desktop-recheck/desktop-redaction-boundaries-green.trx`.
The whitespace check passed. The recorded full UI smoke predates this last
regex-only extension; integrated-worktree builds, UI runs and GitHub CI are
separate verification boundaries and are not claimed here.

These receipts cover Windows service behavior and an actual Avalonia desktop
UI. Native screen-reader certification and Linux/macOS UI behavior remain
unverified by this increment.
