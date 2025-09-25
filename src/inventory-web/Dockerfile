# ---------- BUILD ----------
FROM node:18-alpine AS build
WORKDIR /app

# Dépendances
COPY package*.json ./
RUN npm ci --no-audit --no-fund

# Sources
COPY . .
RUN npm run build

# ---------- RUNTIME ----------
FROM nginx:1.27-alpine

# OpenSSL pour générer le certificat au démarrage
RUN apk add --no-cache openssl

# Contenu statique construit
COPY --from=build /app/dist /usr/share/nginx/html

# Conf nginx (HTTPS + proxy /api)
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Script de génération auto du cert
COPY docker-entrypoint.d/50-ssl-selfsigned.sh /docker-entrypoint.d/50-ssl-selfsigned.sh
RUN chmod +x /docker-entrypoint.d/50-ssl-selfsigned.sh && mkdir -p /etc/nginx/certs

EXPOSE 80 443

# Healthcheck simple : passe si Nginx répond (redirection 301 ok)
HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD wget -qO- http://localhost/ || exit 1
