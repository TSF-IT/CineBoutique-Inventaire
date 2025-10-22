# syntax=docker/dockerfile:1.6
ARG BASE_REG=public.ecr.aws/docker/library

# ---------- BUILD ----------
FROM ${BASE_REG}/node:22-alpine AS build
WORKDIR /repo

# On copie TOUT le contexte (racine du repo OU dossier web)
COPY . .

# Build unique qui s'adapte au contexte :
#  - Cas A (monorepo) : package.json racine avec "workspaces" ET dossier src/inventory-web présent
#  - Cas B (local)    : contexte = src/inventory-web (pas de sous-dossier src/inventory-web)
RUN set -eux; \
  if [ -f package.json ] && grep -q '"workspaces"' package.json && [ -d src/inventory-web ]; then \
    echo "[build] Monorepo detected -> npm ci at root + workspaces build"; \
    (npm ci --no-audit --no-fund || npm install --no-audit --no-fund); \
    npm -w src/inventory-web run build; \
    mkdir -p /out && mv src/inventory-web/dist /out/dist; \
  else \
    echo "[build] Local app detected -> npm ci in current dir"; \
    (npm ci --no-audit --no-fund || npm install --no-audit --no-fund); \
    npm run build; \
    mkdir -p /out && mv dist /out/dist; \
  fi

# ---------- RUNTIME ----------
FROM ${BASE_REG}/nginx:1.28-alpine
RUN apk add --no-cache openssl
COPY --from=build /out/dist /usr/share/nginx/html
