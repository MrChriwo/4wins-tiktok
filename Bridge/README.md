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
