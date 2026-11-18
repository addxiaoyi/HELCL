# Remote follow-up verification — 2026-11-18

This continues the four requested follow-ups from `backend-hardening-2026-11-18.md`.
The user selected GitHub Actions for acceptance. The main branch and the original
dirty working tree are preserved; an isolated test checkout receives an explicit
source allowlist and a secret-pattern scan before publication.

## Implemented

- Mobile pairing screen under My → Remote computer, explicit fingerprint
  confirmation, read-only/cancel permission selection, local revoke, concealed
  invitation input, snapshot tasks and safe loading/error/cancellation states.
- AES-GCM invitation updates using an independent reader token and encryption
  key. The relay retains only the token hash and bounded ciphertext. Updates bind
  to the channel, monotonic revision, same host/client and original relay.
- Separate reader rate limits, body/cipher bounds, durable channel revocation,
  restart-safe host metadata and explicit multi-step cleanup failures.
- Native Linux/macOS credential verification tools and a private keyring/keychain
  session for every run. The tool checks native roundtrip, failed-account-save
  preservation, device identity/trust restoration and cleanup.
- A real Docker/Nginx HTTPS/WSS acceptance harness with an explicit temporary CA,
  non-root state-volume/health tests, restart/revocation/feed checks and labeled
  cleanup. It uses a minimal source build context instead of the entire workspace.

## GitHub acceptance

Three workflows support the dedicated `codex/remote-followup-ci-*` test branch:

- `native-credentials.yml`: native Ubuntu 24.04 and macOS 15 key stores.
- `relay-acceptance.yml`: Docker build, Nginx HTTPS/WSS and persistent revocation.
- `remote-mobile-acceptance.yml`: production mobile controller/portable tests,
  real preview build and desktop-hosted pairing screenshots at three sizes.

Run IDs, conclusions and downloaded reports must be recorded after the workflows
finish. Workflow source preparation or a local mock is not a GitHub pass.

## Local evidence and limits

- The native Linux verifier passed in this machine's Ubuntu WSL with a private
  libsecret/keyring session: `artifacts/native-credentials-linux-wsl.json`.
- Mobile controller tests passed, including late responses, rebinding, revocation,
  subscription replay, cancellation and permission limits. Preview evidence is
  under `TestResults/mobile-remote`; it is explicitly desktop-hosted with fixture
  task I/O, not an Android or public remote-control result.
- Docker Desktop recovery was attempted. Starting its service required
  administrator access; two Desktop start paths failed with an AF_UNIX bind
  collision at `sailor-ingest.sock`. No blind file deletion, global config reset,
  remote Docker connection or business-container restart was performed. Receipt:
  `artifacts/backend-hardening/relay-docker-local-20261118.json`.
- GitHub's isolated container network proves actual HTTPS/WSS and proxy behavior
  on that runner. It cannot establish connectivity between two public physical
  machines, and it does not establish Android input/GPU/native UI behavior.
- No production endpoint, external tunnel or public deployment is created by
  these test workflows. Test credentials and CA material are disposable fixtures.
