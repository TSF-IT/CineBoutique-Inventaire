#!/bin/sh
set -eu

CADDYFILE="/tmp/Caddyfile"
DOMAIN="${DOMAIN:-}"
EMAIL="${ACME_EMAIL:-}"

write_common_server_block() {
  cat <<'CONF' >>"$CADDYFILE"
    encode zstd gzip
    handle_path /api/* {
        reverse_proxy api:8080
    }
    handle {
        reverse_proxy web:80
    }
CONF
}

: >"$CADDYFILE"

if [ -n "$DOMAIN" ]; then
  cat <<CONF >"$CADDYFILE"
$DOMAIN {
CONF
  if [ -n "$EMAIL" ]; then
    cat <<CONF >>"$CADDYFILE"
    tls $EMAIL
CONF
  fi
  write_common_server_block
  cat <<CONF >>"$CADDYFILE"
}
CONF
  cat <<CONF >>"$CADDYFILE"
http://:80 {
    redir https://$DOMAIN{uri} permanent
}
CONF
else
  if [ ! -f /certs/cert.pem ] || [ ! -f /certs/key.pem ]; then
    echo "Certificats manquants dans ./certs (cert.pem et key.pem)" >&2
    exit 1
  fi
  cat <<CONF >>"$CADDYFILE"
:443 {
    tls /certs/cert.pem /certs/key.pem
CONF
  write_common_server_block
  cat <<'CONF' >>"$CADDYFILE"
}

http://:80 {
    redir https://{host}{uri} permanent
}
CONF
fi

exec caddy run --config "$CADDYFILE" --adapter caddyfile
