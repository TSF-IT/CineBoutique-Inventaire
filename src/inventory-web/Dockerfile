# ---------- BUILD ----------
# Paramètre: registre des images "library" (par défaut: ECR public, évite Docker Hub)
ARG BASE_REG=public.ecr.aws/docker/library
FROM ${BASE_REG}/node:22-alpine AS build
WORKDIR /app
RUN npm i -g npm@11.6.2 && npm -v

# Dépendances
COPY package*.json ./
RUN if [ -f package-lock.json ]; then \
      echo "Found lockfile -> npm ci" && npm ci --no-audit --no-fund ; \
    else \
      echo "No lockfile -> npm install" && npm install --no-audit --no-fund ; \
    fi

# Sources
COPY . .
RUN npm run build

# ---------- RUNTIME ----------
ARG BASE_REG=public.ecr.aws/docker/library
FROM ${BASE_REG}/nginx:1.28-alpine

# OpenSSL pour générer le certificat au démarrage
RUN apk add --no-cache openssl

# Contenu statique construit
COPY --from=build /app/dist /usr/share/nginx/html

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
