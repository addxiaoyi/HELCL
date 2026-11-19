# Local diagnostic bundle

Open **WPF Settings → Launcher → Diagnostic bundle**, **Avalonia Desktop Settings → Preview and export diagnostic bundle**, or **Mobile More → Preview and export diagnostic bundle** to select the fields to include, refresh the JSON preview, and save a ZIP file to a location you choose. Nothing is uploaded.

The bundle is a single `diagnostics.json` file with a 16 KiB uncompressed size limit. It can include the app version, operating system family, process architecture, .NET major/minor version, available storage, installed-instance count, launch-preflight codes, download-mirror status, and up to eight recent failure categories from the current app process.

The launcher does not copy account information, instance names, full paths, device identifiers, credentials, raw logs, server URLs, or exception messages into this bundle. Launch checks retain only stable check codes and severity. Recent failures retain only a broad error category. Mirror information retains approved provider IDs, availability, and latency; the optional check makes bounded anonymous probe requests.

Select sections and optionally uncheck individual fields before export. The ZIP is generated from exactly the displayed preview. Review it before saving or sharing. Export errors remain visible and do not trigger a network upload.

Desktop local-file export writes a complete package to a random file in the destination directory and then promotes it with `File.Replace`/`File.Move`. A failed promotion preserves the old destination and removes the temporary package. Mobile document-provider export generates the package in memory before opening the destination; provider I/O failure is still reported and cannot promise atomic replacement on every Android document provider.

## GUI integration baseline

The compact backend PR base does not contain the WPF and Avalonia Desktop GUI foundation. These two pre-existing files were copied read-only from the original workspace only to apply the minimal entry wiring; the original workspace was not changed:

- `src/HelL/Pages/SettingsPage.cs`: original SHA-256 `96FEA33B0CC580D55E8D538E8AB89099DFD8879D5C153CDEF9C61A512FBEEC42`; one diagnostic field added to the existing launcher card.
- `src/HelL.Desktop/Views/SettingsAppearanceView.axaml.cs`: original SHA-256 `917E1D259A877FBEABC821921A50340F43C11F311E53221C3158347539784D0A`; a button and modal-window handler added during initialization.

The WPF diagnostic panel and Avalonia Desktop diagnostic window are new independent files. Full application builds use a disposable copy of the original GUI foundation with these changes overlaid. Build evidence does not claim that the compact backend base contains the rest of that GUI foundation.
