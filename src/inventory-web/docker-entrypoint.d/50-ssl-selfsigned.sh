#!/usr/bin/env sh
set -eu

CERT_DIR="/etc/nginx/certs"
CRT="$CERT_DIR/self.crt"
KEY="$CERT_DIR/self.key"

# IP ou nom affiché dans le certificat (SubjectAltName)
HOST="${PUBLIC_HOST:-localhost}"
DAYS="${SSL_DAYS:-3650}"

mkdir -p "$CERT_DIR"

if [ ! -s "$CRT" ] || [ ! -s "$KEY" ]; then
  echo "[web] Génération d’un certificat auto-signé pour: $HOST (validité ${DAYS}j)"
  # OpenSSL 1.1.1+ : -addext pour SAN. Si ça échoue, on tente un fallback sans SAN.
  if ! openssl req -x509 -nodes -newkey rsa:2048 -sha256 -days "$DAYS" \
      -keyout "$KEY" -out "$CRT" -subj "/CN=$HOST" \
      -addext "subjectAltName=DNS:$HOST,IP:$HOST" >/dev/null 2>&1; then
    openssl req -x509 -nodes -newkey rsa:2048 -sha256 -days "$DAYS" \
      -keyout "$KEY" -out "$CRT" -subj "/CN=$HOST" >/dev/null 2>&1
  fi
  chmod 600 "$KEY"
  echo "[web] Certificat auto-signé généré."
fi
