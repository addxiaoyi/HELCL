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

All three acceptance directions have actual completed/success runs, and their
artifacts have been downloaded and checked:

- [Native credentials: 44319139853](https://github.com/addxiaoyi/HELCL/actions/runs/44319139853),
  commit `781f246ed643bdfe29f3cf2003757cc000ec9888`: Ubuntu 24.04 and macOS 15
  **Intel** passed. Both reports have `ok: true`, all seven native checkpoints,
  private-key signing/PFX export/reimport and both cleanup flags true, with no
  failures. Summary: `TestResults/backend-hardening/native-verified-44319139853.json`.
- [Relay containers: 44320637860](https://github.com/addxiaoyi/HELCL/actions/runs/44320637860),
  commit `83e2b2b8dfbe5c5d9c0d1298d2b3017a5838544d`: **84/84** Remote tests,
  **7/7** container safeguards and all seven actual container checks passed.
  Dockerfile build, non-root/state/health, CA rejection and trust, HTTPS/WSS,
  restart revocation and encrypted-feed persistence ran successfully. Four image
  metadata records and ten owned-resource cleanup entries were checked. Summary:
  `TestResults/remote-followup/relay-44320637860/verification.json`.
- [Mobile pairing: 44320637736](https://github.com/addxiaoyi/HELCL/actions/runs/44320637736),
  commit `83e2b2b8dfbe5c5d9c0d1298d2b3017a5838544d`: **28/28** Mobile and
  **223/223** Portable tests passed. Both builds succeeded, retaining 16 existing
  Core warnings each. Three desktop-hosted preview reports passed at 320×720,
  390×844 and 844×390; all 24 PNG signatures and dimensions were checked. Summary:
  `TestResults/github-mobile-44320637736/verification.json`.

The final test branch is `codex/remote-followup-ci-20261118-01`. Its final head is
`83e2b2b8dfbe5c5d9c0d1298d2b3017a5838544d`. Compared with the native-verified
`781f246` commit, only Docker harness Python/test files and snapshot metadata
changed; native-compiled inputs were checked unchanged. No native pass is inferred
from a different application implementation. Consolidated receipt:
`TestResults/remote-followup/github-final.json`.

The remote main ref was verified unchanged at
`e82bf4a48db6b12d4851015000e112122287c9bb`. The original working tree remains on
`master` at `7fd46e2283f27a964082a0f6bf1a67e077bb75d6`; no source merge, release or
production deployment was performed.

## Defects found by real runs

- Initial source preparation omitted the extensionless Dockerfile. It is now an
  explicit required path, with a missing-source regression.
- Existing mobile/account failure fixtures assumed Windows file-sharing would
  prevent replacement on Unix. Cross-platform deterministic obstacles now retain
  every rollback assertion. Unix account tests still fail after credential sealing
  and check newly written keys are compensated, rather than only testing an early
  read failure. Production save logic was not weakened to make tests pass.
- macOS actually rejected `EphemeralKeySet` during PFX loading. Both identity
  paths now share supported platform-specific import flags without `PersistKeySet`;
  Linux remains ephemeral and Windows retains its Schannel user-key policy. The
  rerun verified real signing, export, reimport and key-store recovery.
- A queued WebSocket writer could acquire its lock before linked cancellation
  propagated. A deterministic cancellation-order regression failed first; starting
  a send is now serialized with disposal. Existing send-count assertions remain.
- Docker rejected a static proxy IP on an automatically configured network. The
  test reuses Docker's non-conflicting CIDR as an explicit, run-owned subnet;
  actual HTTPS/WSS acceptance then passed without relaxing TLS or cleanup checks.

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
- The first macOS ARM job never acquired a runner and was capacity-cancelled
  before executing a step. The successful native run is Intel macOS, not an ARM
  result. Both the capacity failure and pre-fix Intel failure remain as evidence.
- GitHub's isolated container network proves actual HTTPS/WSS and proxy behavior
  on that runner. It cannot establish connectivity between two public physical
  machines, and it does not establish Android input/GPU/native UI behavior.
- No production endpoint, external tunnel or public deployment is created by
  these test workflows. Test credentials and CA material are disposable fixtures.
