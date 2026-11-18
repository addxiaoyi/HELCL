# Native credential and identity verification

`native-credentials.yml` runs the native verifier on `ubuntu-24.04` and `macos-15`.
Its `workflow_dispatch` has no inputs: both matrix jobs run on the selected ref.

The verifier uses `SystemSecretStore.Create()`, not a memory key-store replacement.
Each run prefixes every service name with a fresh `HelL native verification` UUID.
It only looks up keys in that namespace, tracks every created entry and removes
those entries in `finally`. Reports contain checks and platform metadata, never
credentials, private-key exports, pairing fingerprints or session tokens.

## Checks

- Store, update, read and remove a native key, including removal of an absent key.
- Save a large fictional account encrypted by a short native-stored key, reopen it
  and verify that the file contains no fictional token plaintext.
- Reject an injected key-store write and verify that the previous native key and
  encrypted account remain usable. This is an injected failure boundary; it does
  not claim that a real operating-system keychain was locked.
- Hold the account file with `FileShare.None`, attempt add, replace, remove and
  switch operations, then verify the file, selected account, in-memory collection
  and native-stored account credentials are unchanged.
- Reopen a native-backed encrypted device identity and pairing, check that the
  private key matches its original identity, inject a rejected permission update,
  and verify the old identity and read-only permissions still restore.
- Persist revocation and remove all native verification entries before reporting
  success.

The Linux launcher script creates a new D-Bus session and private XDG directories
for an isolated `gnome-keyring-daemon`. It does not open the desktop login keyring.
The macOS script creates a temporary keychain with an empty fixture password,
temporarily selects only that keychain, then restores the previous default and
search list and deletes the temporary keychain. Outside GitHub Actions, macOS
requires a dedicated test account and `HELL_NATIVE_KEYCHAIN_ISOLATED=1`; do not run
it in an active everyday keychain session.

## Run locally

Linux needs .NET SDK 10, `libsecret-tools`, `gnome-keyring`, and `dbus-x11`:

```bash
bash tools/run_native_credentials.sh \
  "$PWD/artifacts/native-credentials-report.json" \
  /tmp/hell-native-credentials-build
```

On an authorized macOS test account with .NET SDK 10:

```bash
HELL_NATIVE_KEYCHAIN_ISOLATED=1 bash tools/run_native_credentials.sh \
  "$PWD/artifacts/native-credentials-report.json" \
  /tmp/hell-native-credentials-build
```

The build uses an independent artifacts directory. `ok: true` is emitted only
after native credential cleanup succeeds. An unsupported OS or mismatched
platform argument fails rather than silently skipping native verification.

## Evidence boundary

Memory-store unit tests establish controlled transaction behavior. They do not
establish native credential access. A Linux report from WSL establishes native
Linux/libsecret behavior inside the isolated session; it does not prove desktop
unlock prompts or a macOS keychain. A macOS claim requires the macOS matrix job
or a matching report from an explicitly authorized macOS host. Merely preparing
this workflow is not evidence that either job has run.
