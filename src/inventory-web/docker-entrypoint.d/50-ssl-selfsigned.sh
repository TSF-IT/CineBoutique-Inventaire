#!/bin/sh
set -eu

# Génère un cert auto-signé si absent (pour l'image nginx de dev)
if [ ! -f /etc/nginx/certs/selfsigned.key ]; then
  mkdir -p /etc/nginx/certs
  openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
    -keyout /etc/nginx/certs/selfsigned.key \
    -out /etc/nginx/certs/selfsigned.crt \
    -subj "/CN=localhost"
fi

exit 0
