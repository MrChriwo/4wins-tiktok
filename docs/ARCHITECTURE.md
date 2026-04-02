# Architecture Guide

This document explains the current architecture of the 4Wins TikTok project from a systems perspective.

---

## 1) System Overview

The solution has two bounded contexts:

1. **Unity Client**
   - Handles game scenes, UI, board state, registration UX, and turn orchestration.
   - Consumes normalized chat/gift events through `TikTokLiveChatAdapter`.

2. **Bridge Server (Node.js/Express)**
   - Connects to TikTok live stream via `tiktok-live-connector`.
   - Buffers events per host session and exposes polling endpoints.
   - Manages session lifecycle and inactivity cleanup.

The Unity client can run in:
- **Direct mode** (TikTok SDK directly from Unity)
- **Bridge mode** (recommended for WebGL and controlled networking behavior)

---

## 2) Unity Domain Architecture

### Scene Flow

1. **Start Scene**
   - `StartScreenController`
   - Reads persisted settings from `PlayerPrefs`.
   - Connects adapter with configured streamer host.
   - Preloads registration scene.

2. **Registration Scene**
   - `RegistrationSceneController`
   - Creates/clears participant registry for new session.
   - Registers participants from matching gift events.
   - Stores participant snapshots for continuity into gameplay.

3. **Gameplay Scene**
   - `GameFlowController` owns the state machine.
   - `ConnectFourUIController` is event-driven and view-centric.

### Core Gameplay State

`GameFlowController` emits and coordinates:
- board initialization and updates
- turn transitions (`Community`, `Streamer`, `Bot`, `GameOver`)
- timer updates for active phases
- round and match scoring
- participant turn ownership and current active participant

### Data Ownership in Unity

- **Board state**: `BoardState` inside `GameFlowController`
- **Participants**: `ParticipantRegistryService`
- **UI state**: projected from game events in `ConnectFourUIController`
- **Config values**: ScriptableObjects + `PlayerPrefs` runtime values

---

## 3) Bridge Domain Architecture

### Core Modules

- `src/index.mjs`
  - bootstrap, sweeper interval, app startup
- `src/app.mjs`
  - express setup + router mount
- `src/routes/bridgeRoutes.mjs`
  - all bridge endpoints
- `src/tiktokSession.mjs`
  - connector creation, event normalization/filtering, connect/disconnect behavior
- `src/sessionStore.mjs`
  - per-host session map, event buffering, inactivity tracking, lifecycle reset/delete

### Session Model (in-memory)

Per `hostId`, session stores:
- `connected`, `connecting`
- connector instance reference
- registration gift normalization state
- registered user aliases set
- event cursor + event buffer
- timestamps (`lastPolledAt`, `lastConnectedAt`)

### Inactivity Expiration

`index.mjs` runs a sweep every 5s:
- calculates inactivity using max(`lastPolledAt`, `lastConnectedAt`)
- expires sessions exceeding configured timeout
- disconnects and performs lifecycle cleanup

This ensures no stale registration/event state survives past session end.

---

## 4) Event Pipeline

### Registration path

1. Unity opens registration scene.
2. Bridge receives TikTok gifts.
3. Gift names are normalized and matched against required registration gift.
4. Matching users are added to session `registeredUsers`.
5. Gift events are polled by Unity adapter and forwarded to registration controller.
6. Registration controller adds users into `ParticipantRegistryService`.

### Move path

1. During community turn, active participant sends `1..7` in chat.
2. Bridge emits chat events only for registered users.
3. Unity adapter forwards chat events to `GameFlowController`.
4. `GameFlowController` validates active user and move legality.
5. Valid move mutates board and triggers scene/UI updates.

### Streamer turn path

1. `GameFlowController` enters streamer turn state.
2. Streamer timer starts.
3. Move can be submitted by world interaction/controls.
4. On timeout, fallback auto-move logic is applied (current implementation).

---

## 5) API Contract (Bridge)

Base: `/bridge`

- `POST /connect`
  - Input: `hostId`, optional `registrationGiftName`
  - Effect: starts connector session for host

- `GET /events?hostId=<id>&after=<cursor>`
  - Polling endpoint returning ordered buffered events after cursor

- `POST /disconnect`
  - Explicit lifecycle end for host session

- `GET /status`
  - Lightweight status snapshot for diagnostics

- `GET /avatar`
  - Optional image proxy/conversion endpoint to improve Unity avatar loading compatibility

Non-bridge:
- `GET /health`

---

## 6) Deployment Topologies

### Local Development
- `Bridge/compose.dev.yaml`
- host port mapping `3010:3010`
- no Traefik dependency

### Production / Reverse Proxy
- `Bridge/compose.yaml`
- exposed internally (`expose`) and routed via Traefik labels
- HTTPS host routing done at proxy edge

Important: DNS + TLS must both be valid for the configured hostname; otherwise Unity bridge connect will fail.

---

## 7) Reliability and Safety Notes

- Session cleanup is explicit and required to avoid state bleed between streams.
- Bridge event buffering is bounded by `BRIDGE_EVENT_BUFFER_SIZE`.
- Avatar proxy includes timeout + format normalization with fallback paths.
- Connect endpoint special-cases known TikTok websocket-upgrade refusal for clearer behavior.

---

## 8) Extension Points

Likely safe extension points:
- Alternate registration rules (gift tiers, keywords, moderation rules)
- Additional admin commands in bridge and Unity handlers
- Persistent session/event storage (Redis/DB) if horizontal scaling is needed
- Observability (structured metrics around connect latency, poll intervals, dropped events)

---

## 9) Operational Checklist

When diagnosing runtime issues, verify in this order:

1. DNS resolution for bridge host
2. TLS handshake + certificate SAN coverage
3. `/health` and `/bridge/status` reachability
4. Bridge connect logs for host
5. Polling cadence and cursor increments
6. Registration gift matching logs
7. Session expiry/cleanup logs

---

## 10) Non-Goals (current implementation)

- Persistent storage of sessions across restarts
- Multi-node bridge clustering
- Guaranteed delivery semantics beyond bounded in-memory polling

Current design favors simple operation and fast local iteration.
