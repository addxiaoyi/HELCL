# Backend persistence and transfer boundaries

This follow-up protects the state shared by Desktop, Mobile and WPF when a write fails, credentials rotate, an installation is incomplete or a download is cancelled.

## Persistence

- Desktop and Mobile AI profiles commit configuration only after the new credential has been written and read back. Failed configuration writes preserve the previous file, in-memory fields and revision; failed credential writes remove the attempted key. A successful commit removes the superseded key. Reads hold the same lock through credential lookup, so rotation cannot produce a mixed profile or remove a key while it is being read. Cleanup diagnostics contain exception types rather than potentially sensitive messages, and profile descriptions omit API keys.
- Instance selection and pinning use the existing atomic configuration commit. Failed writes no longer leave a new selection or revision in memory.
- Instance metadata edits require a JSON object with valid UTF-8. Corrupt bytes are preserved for recovery. Reference version and loader are written together while retaining unrelated fields. Renaming validates metadata before moving the directory or updating selection/pins.
- Cover replacement validates metadata first and retains the old image until metadata commits. Failure restores the previous cover, or removes a newly created cover. A failed rollback retains its backup and reports both errors.

## Downloads and resource targets

- CurseForge keys are attached only to HTTPS requests to exactly `api.curseforge.com`, on the standard port, without user information, under `/v1/`. Download-generated headers and explicitly supplied API headers use this policy. Mirrors receive no user key. Authenticated API requests reject automatic redirects; download redirects continue through per-target header validation.
- Resource targets reuse launch validation for inherited client JARs, pending installations and recovery journals. A reference-only filter remains available for uninstalled mobile instances, but cannot bypass an actual or interrupted installation. The public `InvalidOperationException` contract for incomplete targets is retained.
- Cancellation thrown by the first single-file progress callback now reaches the failure cleanup and stops the tracker before any request is sent.

## Regression evidence

Local evidence lives under the untracked `TestResults/backend-hardening-20261123/` directory. Fixtures use generated instance directories and fake credentials; HTTP transport checks use a loopback replay server.

- `provider-baseline-red.trx`: 5 failures against the previous profiles, including memory/revision drift, residual credentials and secret-bearing descriptions. `selection-baseline-red.trx`: 2 failures against previous selection/pinning, demonstrating changed selection and revision after a failed write.
- `boundary-red.trx`: 20 failures out of 21 cases covering credential URL matching, inherited targets, reference-filter bypass and initial cancellation. The initial suite also exposed a case-sensitive official-host match.
- `transport-red.trx`: all 4 explicit-header/redirect cases failed before transport enforcement. `metadata-utf8-red.trx`: invalid UTF-8 was silently replaced before strict decoding.
- `review-persistence-red.trx` and `provider-read-red.trx` exposed partial rename, lost cover and concurrent profile-read failures. `cover-baseline-red.trx` rechecks the final cover assertions against the previous implementation: 3 failures and 2 successful-path passes.
- Windows write failures use the project's `PermissionError`; fixtures accept the documented filesystem error families and still assert unchanged bytes, selected instance, revision and referenced credentials. Existing assertions, including the interrupted-target exception contract, remain intact.

Final local suites pass: Portable **390** (one platform skip), Mobile **69**, Desktop **29** (one Unix-only skip on Windows), Remote **122**, and WPF **818**. There are **51 new regression cases**. WPF's full nonincremental build passes with zero warnings/errors; an earlier incremental attempt had a missing `obj/Release/net48/HelL.exe`, resolved by the full rebuild before rerunning all 818 WPF tests. The isolated Desktop build passes with the existing 16 Core platform/obsolete-API warnings. `git diff --check` passes.

The dedicated Bugbot interface was unavailable; an independent source-review agent found the rename, cover and concurrent-read follow-ups and verified their final repairs without further blockers. This is not a formal Bugbot scan. Latest cross-platform service, WPF, Desktop smoke, Mobile preview and native Android CI receipts are attached to the current commit of [draft PR #11](https://github.com/addxiaoyi/HELCL/pull/11).

Linux CI also exposed a fixture shutdown difference: a stopped `HttpListener` completes the pending accept with `ObjectDisposedException`, whereas Windows reports `HttpListenerException`. Cleanup accepts both after stopping the listener; the credential and redirect assertions remain unchanged.

These regressions verify controlled failure handling. They do not certify real upstream pagination, a real game installation, OS credential-provider outages or crash recovery across multiple files. Previous native-window and reading acceptance remains documented in `frontend-reading-desktop.md` and `frontend-reading-2026-11-20.md`.
