#!/usr/bin/env bash
# Starts the catalogue stub and the storefront. A reverse proxy still has to sit in front of
# both; see README.md.
set -euo pipefail

: "${NG_ALLOWED_HOSTS:?set this to the public hostname, comma separated}"
: "${PORT:=4000}"
: "${STUB_API_PORT:=5200}"

node api/catalog/stub-api.mjs --port "$STUB_API_PORT" &
STUB=$!
trap 'kill $STUB 2>/dev/null || true' EXIT

SSR_API_ORIGIN="http://127.0.0.1:${STUB_API_PORT}" PORT="$PORT" \
  node server/server.mjs
