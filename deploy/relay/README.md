# HelL private relay

The relay forwards binary traffic between two authenticated endpoints. Room tickets
expire after 24 hours; a room allows one host and one client. Device authentication
and command authorization belong to the endpoints. The inner connection uses TLS
1.3 with pinned device certificates; terminating public HTTPS must not terminate
this inner TLS session.

Generate separate secrets with `openssl rand -base64 32`, then set
`HELL_RELAY_OPERATOR_TOKEN` and `HELL_RELAY_SIGNING_KEY` in your deployment
environment. Also set `HELL_RELAY_TRUSTED_PROXY_IP` to the literal source IP of
your reverse proxy as seen by the container (a host proxy normally uses the Docker
bridge gateway; inspect your actual network rather than assuming its address).
Do not commit secrets. From this directory, run
`docker compose up --build -d`. The service binds to loopback; place your existing
HTTPS reverse proxy in front of port 8080 with WebSocket upgrades enabled. Public
clients use an `https://` base URL; the client library derives `wss://` tunnel URLs.
Configure the proxy to overwrite `X-Forwarded-For` and `X-Forwarded-Proto` and
forward WebSocket upgrades. Only explicitly configured `Relay:KnownProxies` are
trusted, with one forwarded hop. Do not trust arbitrary Internet ranges. No public
server is deployed by these files.

`GET /health` reports readiness. An operator calls `POST /v1/rooms` with
`Authorization: Bearer <operator-token>` to obtain a room and separate host/client
tickets. Each endpoint connects to `/v1/tunnel` using its ticket in the same header.
Tickets must never appear in query strings, logs, or analytics. Share only the
client ticket with the paired client; the operator token stays on the host.

An operator can call `DELETE /v1/rooms/{room}` to revoke one room. A successful
request returns 204, closes both active sockets, and rejects either old ticket
until its expiry, including after a restart when the state volume is retained.
Repeating the revoke is idempotent while its tombstone is retained; an unknown or
expired room returns 404. Anonymous callers receive 401.

`POST /v1/rooms/{room}/renew` uses the same operator bearer and returns a new
`RelayRoom`. It atomically revokes the old room and creates a different room ID
with fresh 24-hour tickets; it does not extend old bearer credentials. Distribute
the replacement client ticket over the authenticated pairing channel and take a
new snapshot after reconnecting. `RemoteHostChannel.RevokeRoomAsync` and
`RenewRoomAsync` expose these APIs. If a renewal response is lost, create a new
room; retrying the revoked old room returns 404. The room registry fails closed.

The default budgets are 10 anonymous management/tunnel attempts per source IP
per minute, 60 authenticated management requests per operator per minute, and 60
connection attempts per room role per minute. Anonymous requests cannot consume
the operator's budget. Each role has at most one reserved connection; admission
happens before a WebSocket upgrade. Rate or concurrency rejection returns 429
with `Retry-After`; capacity exhaustion returns 503. Allowlisted room tickets
have their own connection budget instead of consuming the anonymous budget.

At most 128 rooms are active and 4,096 unexpired room records (including revoked
tombstones) are tracked. Creating or renewing rooms fails at registry capacity;
revocation never evicts a live tombstone to make space. Expired records are
removed. An unpaired connection has 60 seconds to pair. After pairing, each
receive/forward operation has a 120-second deadline. Per active room, both
directions together have a 16 MiB / 4,096-fragment fixed-window budget per minute;
zero-length fragments also count. A WebSocket message is limited to 1 MiB.
Exceeding a tunnel budget closes both sockets. These limits are intended for
control traffic and bounded transfers, not continuous media.

Configure limits under `Relay` (`MaxRooms`, `MaxTrackedRooms`,
`ConnectionsPerWindow`, `ConnectionWindow`, `PairingTimeout`, `IdleTimeout`,
`TrafficWindow`, `BytesPerWindow`, `FragmentsPerWindow`,
`AnonymousRequestsPerMinute`, `OperatorRequestsPerMinute`). Time spans use the
.NET configuration form, for example `Relay__IdleTimeout=00:02:00`. Clients use
independent 10-second WebSocket-connect and TLS-handshake deadlines and a
30-second application request deadline; `RemoteConnectionOptions` customizes
these without changing the existing method signatures.

Compose persists the bounded room registry at `/state/rooms.json` in the
`relay-state` volume. It contains room IDs, expiries and revocation flags, never
tickets or device private keys. Writes are atomic and flushed before a successful
issuance/revocation response. A missing registry allows no previously issued
tickets, and a corrupt or unwritable registry prevents startup. Preserve this
volume across upgrades; do not share it between simultaneously running relay
instances. This is a single-instance service, not a distributed room store.
Without `Relay:StatePath`, development/test deployments use an in-memory registry
and invalidate every outstanding ticket on restart.

The container runs without Linux capabilities, with a read-only root filesystem,
256 MiB memory, one CPU, 128 processes and a 32 MiB temporary filesystem. Its
health check invokes `dotnet HelL.Relay.dll --healthcheck` against internal port
8080, so no shell, curl or extra health-check package is required. After starting,
check `docker compose ps` for healthy state and test HTTPS/WebSocket upgrades
through your actual reverse proxy. A successful local test is not proof that the
public proxy, certificate or firewall is configured.

The channel carries control messages and bounded application transfers. It does
not capture a desktop, stream game video, or provide remote input to a game.
Both devices must be online. Reconnecting creates a new inner TLS session and
requires a fresh application-state snapshot. Revoking a device must also close
its active host session. Use the room-revocation API for immediate routing
revocation; it does not replace the endpoint's durable paired-device permissions.
Rotating the signing key and restarting the relay invalidates all room tickets.

Current implementation includes authenticated routing, the encrypted transport
library and `RemoteHostChannel` for explicit snapshot/cancel request/reply sessions.
The opt-in host/companion integration manages reconnection, room renewal and
durable paired-device permissions. See [remote companion setup](../../docs/remote-companion.md)
for the helper CLI and host wiring. Pairing UI and real-device acceptance through
a public deployment remain outstanding; this service alone does not enable them.
