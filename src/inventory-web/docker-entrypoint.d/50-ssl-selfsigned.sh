#!/bin/sh
set -eu

CERT_DIR="/etc/nginx/certs"
CRT="$CERT_DIR/selfsigned.crt"
KEY="$CERT_DIR/selfsigned.key"

# Génère un cert auto-signé si absent (pour l'image nginx de dev)
if [ ! -f "$KEY" ] || [ ! -f "$CRT" ]; then
  mkdir -p "$CERT_DIR"
  openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout "$KEY" \
    -out "$CRT" \
    -subj "/CN=localhost"
fi

# Par compat éventuelle, crée des alias si une conf attend encore self.crt/self.key
[ -f "$CERT_DIR/self.crt" ] || ln -sf "$CRT" "$CERT_DIR/self.crt"
[ -f "$CERT_DIR/self.key" ] || ln -sf "$KEY" "$CERT_DIR/self.key"

exit 0
