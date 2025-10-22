# syntax=docker/dockerfile:1.6
ARG BASE_REG=public.ecr.aws/docker/library

# ---------- BUILD ----------
FROM ${BASE_REG}/node:22-alpine AS build
WORKDIR /repo

# Vérif context : on exige les manifestes à la racine du repo
COPY package.json ./package.json
COPY package-lock.json* ./package-lock.json

# Si quelqu’un a lancé le build depuis src/inventory-web, on le détecte et on stoppe
RUN test -f package.json && grep -q "\"workspaces\"" package.json || (echo "ERROR: Build context must be REPO ROOT (workspaces required)" && exit 1)

RUN npm ci --no-audit --no-fund || npm install --no-audit --no-fund

# Copier le workspace web depuis la racine du repo
COPY src/inventory-web ./src/inventory-web

RUN npm -w src/inventory-web run build

# ---------- RUNTIME ----------
FROM ${BASE_REG}/nginx:1.28-alpine

# OpenSSL pour générer le certificat au démarrage
RUN apk add --no-cache openssl

# Contenu statique construit
COPY --from=build /repo/src/inventory-web/dist /usr/share/nginx/html

# Conf nginx (HTTPS + proxy /api)
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Script de génération auto du cert
COPY docker-entrypoint.d/50-ssl-selfsigned.sh /docker-entrypoint.d/50-ssl-selfsigned.sh

# Strip CRLF si jamais Git t’a “aidé”, puis exécutable
RUN sed -i 's/\r$//' /docker-entrypoint.d/50-ssl-selfsigned.sh \
    && chmod +x /docker-entrypoint.d/50-ssl-selfsigned.sh \
    && mkdir -p /etc/nginx/certs

EXPOSE 80 443

# Healthcheck simple : passe si Nginx répond (redirection 301 ok)
HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD wget -qO- http://localhost/ || exit 1
