#!/usr/bin/env bash
set -euo pipefail

REMOTE_HOST="rr-dev-vm-alpha"
REMOTE_BASE_DIR="/home/chriwo"
REMOTE_APP_DIR_NAME="game"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_ROOT="$SCRIPT_DIR"
SOURCE_WEB_DIR="$SOURCE_ROOT/4WinsWeb"
SOURCE_COMPOSE_FILE="$SOURCE_ROOT/compose.web.yaml"
REMOTE_TARGET_DIR="$REMOTE_BASE_DIR/$REMOTE_APP_DIR_NAME"

if ! command -v ssh >/dev/null 2>&1; then
  echo "Error: ssh is not installed." >&2
  exit 1
fi

if ! command -v scp >/dev/null 2>&1; then
  echo "Error: scp is not installed." >&2
  exit 1
fi

if [[ ! -d "$SOURCE_WEB_DIR" ]]; then
  echo "Error: frontend folder not found at '$SOURCE_WEB_DIR'." >&2
  exit 1
fi

if [[ ! -f "$SOURCE_COMPOSE_FILE" ]]; then
  echo "Error: compose file not found at '$SOURCE_COMPOSE_FILE'." >&2
  exit 1
fi

echo "Deploying frontend to ${REMOTE_HOST}:${REMOTE_TARGET_DIR}"

echo "Cleaning remote target..."
ssh "$REMOTE_HOST" "rm -rf '$REMOTE_TARGET_DIR' && mkdir -p '$REMOTE_TARGET_DIR'"

echo "Copying frontend build and compose file via scp..."
scp -r "$SOURCE_WEB_DIR" "${REMOTE_HOST}:${REMOTE_TARGET_DIR}/"
scp "$SOURCE_COMPOSE_FILE" "${REMOTE_HOST}:${REMOTE_TARGET_DIR}/compose.web.yaml"

echo "Done. Remote frontend deployed to ${REMOTE_TARGET_DIR}"
echo "On remote, start with: cd ${REMOTE_TARGET_DIR} && podman compose -f compose.web.yaml up -d"
