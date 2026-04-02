#!/usr/bin/env bash
set -euo pipefail

REMOTE_HOST="rr-dev-vm-alpha"
REMOTE_BASE_DIR="/home/chriwo"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="$SCRIPT_DIR"
REMOTE_TARGET_DIR="$REMOTE_BASE_DIR/$(basename "$SOURCE_DIR")"

if ! command -v ssh >/dev/null 2>&1; then
  echo "Error: ssh is not installed." >&2
  exit 1
fi

if ! command -v scp >/dev/null 2>&1; then
  echo "Error: scp is not installed." >&2
  exit 1
fi

echo "Deploying $(basename "$SOURCE_DIR") to ${REMOTE_HOST}:${REMOTE_TARGET_DIR}"

echo "Cleaning remote target..."
ssh "$REMOTE_HOST" "rm -rf '$REMOTE_TARGET_DIR' && mkdir -p '$REMOTE_BASE_DIR'"

echo "Copying files via scp..."
scp -r "$SOURCE_DIR" "${REMOTE_HOST}:${REMOTE_BASE_DIR}"

echo "Done. Remote folder updated at ${REMOTE_TARGET_DIR}"
