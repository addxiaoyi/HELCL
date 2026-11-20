# WPF interaction details

Baseline: `1fbdeae746f64e5cc001f0726e340151265acde7`.

This increment preserves the existing WPF layout and limits product changes to
account preflight validation and reusable form behavior.

## Confirmed behavior and repair

- A malformed unified-pass server ID previously passed the nonempty check,
  submitted a background login task and closed the form. The form now uses the
  existing `Nide8.NormalizeServerId` before submission. Its format error stays on
  the focused ID field; the username and password remain available. Valid links
  submit the same canonical lowercase ID as the shared backend.
- Login inputs constructed without `Ui.Field` retained their error marker after
  editing, and password edits never cleared it. `FieldState` now subscribes only
  while the input has an error, handles text/password/selection corrections, and
  removes the handler once cleared. `ErrorText` supplies presentation without
  owning that behavior.
- A password-only editor initially focused its submit action, and an asynchronous
  save refusal restored input but lost the editing focus. Dialog input discovery
  includes password boxes; an unsuccessful save returns focus to its invalid or
  previous input. Focus tracking is removed when the dialog unloads, and successful
  submission keeps the established close-and-restore behavior.

## Evidence

The new `WpfDetailCheck` runs through the existing `--frontend-recheck` entry point,
so CI executes it alongside the previous checks. It uses real WPF controls and
the actual `BridgeClient` serialization path with an isolated HTTP handler; no
real authentication, jar download, credentials or account mutation occurs. The
original bridge client is restored in `finally`; the controlled client is disposed,
the fixture task marker is cleared, and completed/cancelled dialogs are awaited.
Every preview uses the existing fresh `TIPCL_HOME` profile and leaves language
selection unchanged.

The baseline run failed seven assertions covering initial/password focus,
correction markers, recovery focus, and malformed-ID submission. The repaired
version passes all 14 new assertions, including retained account fields and one
canonical valid-link submission. The existing recheck still passes its 39 other
assertions (53 combined); `--frontend-remediation` passes its original 17; `--verify`
passes 153. The final UI preview build and complete solution build both report
zero warnings and errors. The invalid-ID screenshot was inspected for field
focus, error visibility, preserved username and a masked password.

Receipts are under the parent workspace's
`TestResults/frontend-details-20261120/wpf/`: `red-ui.log`,
`red/details/wpf-details.json`, `frozen-recheck/details/wpf-details.json`,
`frozen-remediation.log`, `frozen-verify.log` and `full-build.log`.

The first two local full-suite attempts each passed 817/818 and failed a different
plugin-store directory promotion with `Access denied` under `D:\UserCache\Temp`;
the first failed case passed on an isolated rerun. These failures are preserved in
`tests/wpf.trx` and `tests/wpf-confirmed.trx`. Plugin code and assertions were not
changed. A separate run with process-local `TEMP`/`TMP` in this evidence directory
passed **818/818**, exit 0, without changing product code, fixtures or assertions;
its receipt is `tests/wpf-isolated-temp.trx`. The local environment distinction is
recorded rather than treating the earlier file-promotion failures as passed tests.

This increment verifies keyboard focus and WPF rendering, not native screen-reader
output or successful authentication against an external provider.
