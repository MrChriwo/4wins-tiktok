# 4Wins TikTok Bridge

Bridge server for WebGL/Unity clients using `tiktok-live-connector`.

## Endpoints

- `GET /health`
- `POST /bridge/connect` body: `{ "hostId": "streamer_name" }`
- `GET /bridge/events?hostId=streamer_name&after=0`
- `POST /bridge/disconnect` body: `{ "hostId": "streamer_name" }`

## Local Run

```bash
cd Bridge
npm install
npm start
```

## Podman Compose Run

```bash
cd Bridge
podman compose up -d --build
podman compose logs -f bridge
```

Stop:

```bash
cd Bridge
podman compose down
```

Health check:

```bash
curl http://localhost:3010/health
```

## Log Levels

Set `BRIDGE_LOG_LEVEL` to one of: `trace`, `debug`, `info`, `warn`, `error`.
