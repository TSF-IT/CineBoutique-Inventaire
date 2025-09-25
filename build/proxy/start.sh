#!/usr/bin/env sh
set -e

# Env attendues :
# - DOMAIN="inventaire.ton-domaine.tld"  ET  ACME_EMAIL="ops@ton-domaine.tld"  (Let's Encrypt)
#        OU
# - PUBLIC_HOST="192.168.1.50" (ou "inventaire.lan") pour un cert interne (à faire approuver sur les devices)

: "${API_UPSTREAM:=http://api:8080}"
: "${WEB_UPSTREAM:=http://web:80}"
: "${ACME_STAGING:=0}"

cat > /etc/caddy/Caddyfile <<'EOF'
# Ce bloc est réécrit plus bas par ce script.
EOF

mk_caddy_for_domain() {
  local domain="$1" email="$2" staging="$3"

  cat > /etc/caddy/Caddyfile <<EOF
{
  email ${email}
  $( [ "$staging" = "1" ] && echo 'acme_ca https://acme-staging-v02.api.letsencrypt.org/directory' )
}

# HTTP -> HTTPS
:80 {
  redir https://{host}{uri}
}

${domain} {
  encode zstd gzip
  @api path /api* /swagger* /health
  handle @api {
    reverse_proxy ${API_UPSTREAM}
  }
  handle {
    reverse_proxy ${WEB_UPSTREAM}
  }
  log {
    output stdout
    format console
  }
}
EOF
}

mk_caddy_for_internal() {
  local host="$1"
  cat > /etc/caddy/Caddyfile <<EOF
# Certificat interne (CA interne de Caddy) — le device client doit approuver ce CA pour éviter l'alerte
{
  local_certs
}

# HTTP -> HTTPS
:80 {
  redir https://{host}{uri}
}

https://${host} {
  tls internal

  encode zstd gzip

  @api path /api* /swagger* /health
  handle @api {
    reverse_proxy ${API_UPSTREAM}
  }
  handle {
    reverse_proxy ${WEB_UPSTREAM}
  }

  log {
    output stdout
    format console
  }
}
EOF
}

if [ -n "${DOMAIN}" ] && [ -n "${ACME_EMAIL}" ]; then
  echo "[proxy] Mode domaine public avec Let's Encrypt pour ${DOMAIN}"
  mk_caddy_for_domain "${DOMAIN}" "${ACME_EMAIL}" "${ACME_STAGING}"
elif [ -n "${PUBLIC_HOST}" ]; then
  echo "[proxy] Mode interne (certificat Caddy interne) pour ${PUBLIC_HOST}"
  mk_caddy_for_internal "${PUBLIC_HOST}"
else
  echo >&2 "[proxy] ERREUR: vous devez définir DOMAIN+ACME_EMAIL (Let's Encrypt) ou PUBLIC_HOST (IP/hostname interne)."
  exit 1
fi

# Lancement de Caddy
exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
