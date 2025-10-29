#!/usr/bin/env bash
set -euo pipefail

TARGET="${1:-}"
CONF=/home/admintsf/apps/inventaire/nginx/conf.d/api_upstream.conf

if [ "$TARGET" = "blue" ]; then
  cat <<'EOF' > "$CONF"
proxy_pass http://host.docker.internal:8080$request_uri;
EOF
elif [ "$TARGET" = "green" ]; then
  cat <<'EOF' > "$CONF"
proxy_pass http://host.docker.internal:8081$request_uri;
EOF
else
  echo "usage: $0 blue|green" >&2
  exit 1
fi

docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d web
echo "Switched to $TARGET"
