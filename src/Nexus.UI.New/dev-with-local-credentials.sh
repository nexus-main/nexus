#!/usr/bin/env sh
set -eu

CREDENTIALS_FILE="${NEXUS_UI_NEW_CREDENTIALS:-$HOME/.config/nexus-ui-new/credentials.env}"

if [ ! -f "$CREDENTIALS_FILE" ]; then
  printf '%s\n' "Missing credentials file: $CREDENTIALS_FILE" >&2
  printf '%s\n' "Create it with NEXUS_ENDPOINT and NEXUS_TOKEN. Use .env.example as a template." >&2
  exit 1
fi

set -a
. "$CREDENTIALS_FILE"
set +a

export NEXUS_ENDPOINT="${NEXUS_ENDPOINT:-}"
export NEXUS_TOKEN="${NEXUS_TOKEN:-}"

if [ "${1:-}" = "--" ]; then
  shift
fi

exec npm run dev -- "$@"
