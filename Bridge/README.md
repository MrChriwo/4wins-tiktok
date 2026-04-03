# Bridge Service

This directory contains the Node.js bridge used by Unity clients to consume TikTok live events through a polling API.

For full project documentation, see:
- Root overview: `../README.md`
- Deep architecture: `../docs/ARCHITECTURE.md`

---

## Responsibilities

- Connect to TikTok stream by `hostId`
- Normalize gift/chat events
- Maintain per-host in-memory session buffers
- Serve buffered events to Unity pollers (`after` cursor model)
- Expire and cleanup inactive sessions
- Proxy/normalize avatar images for Unity compatibility

---

## API Endpoints

Base route: `/bridge`

- `POST /bridge/connect`
	- Body: `{ "hostId": "streamer_name", "registrationGiftName": "Rose" }`
- `GET /bridge/events?hostId=streamer_name&after=0`
- `POST /bridge/disconnect`
	- Body: `{ "hostId": "streamer_name" }`
- `GET /bridge/status?hostId=streamer_name`
- `GET /bridge/avatar?url=<encoded-avatar-url>`
- `POST /bridge/debug/inject`

Health endpoint:
- `GET /health`

---

## Configuration (ENV)

- `PORT` (default `3010`)
- `BRIDGE_LOG_LEVEL` (default `info`)
- `BRIDGE_EVENT_BUFFER_SIZE` (default `1500`)
- `BRIDGE_REGISTRATION_GIFT_NAME` (default `Rose`)
- `BRIDGE_SESSION_INACTIVITY_TIMEOUT_SECONDS` (default `20`)
- `BRIDGE_ADMIN_DB_PATH` (default `./data/admins.sqlite`)
- `BRIDGE_ADMIN_ASSIGNMENTS` (seed list per streamer, format `streamerA=admin1,admin2;streamerB=mod1`)
- `BRIDGE_GLOBAL_ADMIN_USERNAMES` (comma-separated global admins, e.g. `ichriwo,supermod`)

### Admin authorization (SQLite)

- Admin rights are checked against SQLite table `admin_host_users`.
- Optional global admins are checked against SQLite table `admin_global_users`.
- Effective admin check is: global admin OR host-scoped admin for active `hostId`.
- At startup, assignments from `BRIDGE_ADMIN_ASSIGNMENTS` are inserted with `INSERT OR IGNORE`.
- At startup, `BRIDGE_GLOBAL_ADMIN_USERNAMES` are inserted with `INSERT OR IGNORE`.
- Admin commands are accepted only when issuer is admin for the active `hostId` (case-insensitive, `@` prefix ignored).

Supported commands from chat:

- `/register`
- `/register <username>`
- `/takeover`
- `/kick <username>`

`/ register <username>` (with space after slash) is accepted as well.

### Database creation

No manual migration step is needed.

- On bridge startup, SQLite file is created automatically at `BRIDGE_ADMIN_DB_PATH`.
- Required tables `admin_host_users` and `admin_global_users` are auto-created.
- Seed assignments from `BRIDGE_ADMIN_ASSIGNMENTS` are inserted with `INSERT OR IGNORE`.
- Global admins from `BRIDGE_GLOBAL_ADMIN_USERNAMES` are inserted with `INSERT OR IGNORE`.

Example:

- `BRIDGE_ADMIN_DB_PATH=/app/data/admins.sqlite`
- `BRIDGE_ADMIN_ASSIGNMENTS=streamerA=admin1,admin2;streamerB=mod1`
- `BRIDGE_GLOBAL_ADMIN_USERNAMES=ichriwo`

### Quick test flow

1. Start bridge (dev compose):

```bash
cd Bridge
podman compose -f compose.dev.yaml up -d --build
podman logs -f 4wins-bridge-dev
```

2. Check startup logs for `admin sqlite store initialized` and assignment list.

3. Connect Unity/bridge with `hostId=streamerA`.

4. From TikTok chat:
	- as `admin1`: `/register testuser`, `/takeover`, `/kick testuser` -> must work
	- as global admin (`ichriwo`): same commands for **any hostId** -> must work
	- as non-admin: same commands -> must be ignored

5. Stop/restart bridge and verify commands still work (proves DB persistence via `./data:/app/data`).

---

## Run Modes

### A) Local development (no Traefik)

Uses `compose.dev.yaml`.

```bash
cd Bridge
podman compose -f compose.dev.yaml up -d --build
podman logs -f 4wins-bridge-dev
```

Health check:

```bash
curl http://localhost:3010/health
```

Stop:

```bash
podman compose -f compose.dev.yaml down
```

### B) Reverse-proxy/Traefik mode

Uses `compose.yaml`.

```bash
cd Bridge
podman compose -f compose.yaml up -d --build
```

Notes:
- Service is exposed internally (`expose`) and routed by Traefik labels.
- DNS + TLS must match the configured router host.

---

## Session Lifecycle Behavior

Expected lifecycle:

1. Session starts via `/bridge/connect`
2. Registration/game events are buffered during active polling
3. Session ends on `/bridge/disconnect` or inactivity expiry
4. Session state is fully cleaned so reconnect starts fresh

This prevents stale registration state from leaking into new sessions.

---

## Deployment Helper

`sdeployment.sh` copies the full `Bridge` folder to remote target using `ssh` + `scp`.

Current defaults:
- Host: `rr-dev-vm-alpha`
- Destination base: `/home/chriwo`

Run:

```bash
cd Bridge
./sdeployment.sh
```
