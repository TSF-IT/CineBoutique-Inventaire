# ---------- BUILD ----------
# Paramètre: registre des images "library" (par défaut: ECR public, évite Docker Hub)
FROM node:22-alpine AS build
WORKDIR /repo

# Copier le lock racine et package.json racine
COPY package*.json ./

# Installer à la racine d’après le lockfile racine
RUN npm ci --no-audit --no-fund || npm install --no-audit --no-fund

# Copier le workspace web
COPY src/inventory-web ./src/inventory-web

# Builder le workspace web
RUN npm -w src/inventory-web run build

# ---------- RUNTIME ----------
ARG BASE_REG=public.ecr.aws/docker/library
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
