#!/usr/bin/env bash
set -euo pipefail

show_help() {
  cat <<'EOF'
Usage:
  ./start-dev-servers.sh [--logs|-l]

Description:
  Starts dev server containers from Bridge/compose.dev.yaml,
  always rebuilding and recreating containers in detached mode.

Options:
  -l, --logs   Follow logs after startup (equivalent to compose logs -f)
  -h, --help   Show this help
EOF
}

FOLLOW_LOGS=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    -l|--logs)
      FOLLOW_LOGS=true
      shift
      ;;
    -h|--help)
      show_help
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      show_help
      exit 1
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BRIDGE_DIR="$SCRIPT_DIR/Bridge"
COMPOSE_FILE="compose.dev.yaml"

if [[ ! -d "$BRIDGE_DIR" ]]; then
  echo "Error: Bridge directory not found at '$BRIDGE_DIR'." >&2
  exit 1
fi

if [[ ! -f "$BRIDGE_DIR/$COMPOSE_FILE" ]]; then
  echo "Error: compose file not found at '$BRIDGE_DIR/$COMPOSE_FILE'." >&2
  exit 1
fi

mkdir -p "$BRIDGE_DIR/data"

if command -v podman >/dev/null 2>&1; then
  COMPOSE_CMD=(podman compose)
elif command -v docker >/dev/null 2>&1; then
  COMPOSE_CMD=(docker compose)
else
  echo "Error: neither podman nor docker is installed." >&2
  exit 1
fi

echo "Using: ${COMPOSE_CMD[*]}"
echo "Starting dev servers (build + force recreate + detached)..."
(
  cd "$BRIDGE_DIR"
  "${COMPOSE_CMD[@]}" -f "$COMPOSE_FILE" up -d --build --force-recreate
)

echo "Dev servers started."

if [[ "$FOLLOW_LOGS" == "true" ]]; then
  echo "Following logs... (Ctrl+C to stop following; containers keep running)"
  (
    cd "$BRIDGE_DIR"
    "${COMPOSE_CMD[@]}" -f "$COMPOSE_FILE" logs -f
  )
fi
