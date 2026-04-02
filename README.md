# 4Wins TikTok

Interactive Connect Four game controlled by TikTok Live participants.

The project has two runtime parts:
- **Unity client** (`Assets/_Project`) for UI, gameplay, scene flow, and TikTok adapter integration.
- **Bridge server** (`Bridge`) for reliable TikTok event ingestion and polling APIs used by Unity (especially WebGL/browser deployments).

---

## What This Project Does

- Runs a **streamer vs community** Connect Four match.
- Supports a **registration phase** where users join by sending a configured TikTok gift.
- Rotates through registered participants and lets the active user send moves (`1..7`) in chat.
- Supports bridge-based event transport with buffering and inactivity-based session expiration.
- Includes local and Traefik-based deployment options for the Bridge.

---

## Repository Structure

```text
4wins-tiktok/
├─ Assets/_Project/
│  ├─ Scenes/                    # Unity scenes (Start, Registration, Game)
│  ├─ UI/                        # UXML/USS UI definitions
│  ├─ ScriptableObjects/         # Game/UI configuration assets
│  └─ Scripts/
│     ├─ Bootstrap/              # Start + registration scene controllers
│     ├─ Gameplay/               # Core game state machine and participant flow
│     ├─ TikTok/                 # TikTok adapter + message DTOs
│     ├─ UI/                     # Runtime UI controller and board rendering
│     ├─ Voting/                 # Vote parsing/tallies
│     ├─ Core/                   # Shared enums/types
│     ├─ Config/                 # Config models used by gameplay/UI
│     ├─ AI/                     # Bot strategy abstractions
│     └─ World/                  # World-space board interactions
│
├─ Bridge/
│  ├─ src/                       # Node.js bridge implementation
│  │  ├─ index.mjs               # App bootstrap + inactivity sweeper
│  │  ├─ app.mjs                 # Express app wiring
│  │  ├─ routes/bridgeRoutes.mjs # /bridge API routes
│  │  ├─ tiktokSession.mjs       # TikTok connector event handling
│  │  ├─ sessionStore.mjs        # Session/event buffering lifecycle state
│  │  ├─ config.mjs              # ENV config
│  │  ├─ logger.mjs              # Pino logger
│  │  └─ normalizers.mjs         # Host/user normalization helpers
│  ├─ compose.yaml               # Production/Traefik compose
│  ├─ compose.dev.yaml           # Local development compose
│  ├─ Dockerfile                 # Bridge image
│  ├─ sdeployment.sh             # SCP deployment helper
│  └─ README.md                  # Bridge-specific operational docs
│
└─ ProjectSettings/, Packages/   # Standard Unity project files
```

---

## Runtime Architecture (High-Level)

### Unity side

1. **Start scene** (`StartScreenController`)
   - Loads streamer/game settings from `PlayerPrefs`.
   - Connects TikTok adapter using configured host.
   - Preloads and activates registration scene.

2. **Registration scene** (`RegistrationSceneController`)
   - Clears participant registry at session start.
   - Accepts registrations via gift match (`RegistrationGiftName`).
   - Captures participant snapshot and transitions to gameplay scene.

3. **Game scene** (`GameFlowController` + `ConnectFourUIController`)
   - Manages round/match state machine.
   - Community participant turn timing and streamer turn timing.
   - Emits UI events for board, timer, status, score, and active participant.

### Bridge side

1. Unity calls `POST /bridge/connect` with `hostId` and registration gift name.
2. Bridge creates a host session and opens TikTok connector.
3. Bridge normalizes incoming TikTok events and appends them to an in-memory event buffer.
4. Unity polls `GET /bridge/events?hostId=...&after=...`.
5. Inactivity sweeper expires stale sessions and fully cleans session state.

For detailed sequences and lifecycle guarantees, see `docs/ARCHITECTURE.md`.

---

## Key Unity Modules

- `Assets/_Project/Scripts/Bootstrap/StartScreenController.cs`
  - Launch flow, settings modal, scene loading orchestration.
- `Assets/_Project/Scripts/Bootstrap/RegistrationSceneController.cs`
  - Gift-driven registration and scene transition.
- `Assets/_Project/Scripts/Gameplay/GameFlowController.cs`
  - Core turn logic, timers, state transitions, win/draw handling.
- `Assets/_Project/Scripts/TikTok/TikTokLiveChatAdapter.cs`
  - Bridge/direct TikTok mode, polling, disconnect/reconnect behavior.
- `Assets/_Project/Scripts/UI/ConnectFourUIController.cs`
  - HUD binding, participant list, active player display, board updates.

---

## Bridge API Summary

Base: `/bridge`

- `GET /status?hostId=<id>`
- `POST /connect` body: `{ "hostId": "...", "registrationGiftName": "Rose" }`
- `GET /events?hostId=<id>&after=<eventId>`
- `POST /disconnect` body: `{ "hostId": "..." }`
- `GET /avatar?url=<encoded remote avatar url>`
- `POST /debug/inject` for local/debug event simulation

Health endpoint:
- `GET /health`

---

## Configuration

### Unity (stored via `PlayerPrefs`)

Keys are centralized in:
- `Assets/_Project/Scripts/Bootstrap/BootstrapKeys.cs`

Common runtime values:
- streamer username
- match wins-to-win target
- registration gift name
- registration duration seconds
- participant turn duration seconds
- community/streamer display labels

### Bridge (environment variables)

- `PORT` (default `3010`)
- `BRIDGE_LOG_LEVEL` (default `info`)
- `BRIDGE_EVENT_BUFFER_SIZE` (default `1500`)
- `BRIDGE_REGISTRATION_GIFT_NAME` (default `Rose`)
- `BRIDGE_SESSION_INACTIVITY_TIMEOUT_SECONDS` (default `20`)

---

## Running the Bridge

### Local dev (no Traefik)

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

### Production-style (Traefik)

```bash
cd Bridge
podman compose -f compose.yaml up -d --build
```

Ensure DNS + TLS are valid for the router host configured in `compose.yaml`.

---

## Deployment Helper

`Bridge/sdeployment.sh` copies the full Bridge folder to the remote server target.

Current target:
- SSH host: `rr-dev-vm-alpha`
- destination: `/home/chriwo/Bridge`

Run:

```bash
cd Bridge
./sdeployment.sh
```

---

## Session Lifecycle Guarantees

Current intended lifecycle:

1. **Session start** (`/bridge/connect`)
2. **Registration open/close** (game-controlled registration window)
3. **Active gameplay polling** (`/bridge/events`)
4. **Session end** (manual disconnect or inactivity expiry)
5. **Server cleanup**: session state is destroyed so next connect starts fresh

This prevents stale registrations/events from leaking into a later session.

---

## Troubleshooting

### Unity: `Cannot resolve destination host`
- DNS for configured bridge host is missing or unreachable.

### Unity: `Unable to complete SSL connection`
- TLS/certificate mismatch for host; verify certificate SAN coverage and Cloudflare/Traefik setup.

### No expiry logs
- Expiry only triggers for sessions that are still marked connected/connecting and inactive beyond timeout.

### Registration not arriving in game
- Verify registration scene is active and not already closed.
- Check bridge logs for gift filtering and normalized gift-name matching.

---

## Additional Documentation

- Architecture deep dive: `docs/ARCHITECTURE.md`
- Bridge operations: `Bridge/README.md`
