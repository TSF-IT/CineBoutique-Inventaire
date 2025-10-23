# syntax=docker/dockerfile:1.6
ARG BASE_REG=public.ecr.aws/docker/library

# ---------- BUILD ----------
FROM ${BASE_REG}/node:22-alpine AS build
WORKDIR /repo

# 1) Copie TOUT le contexte (racine OU dossier web)
COPY . .

# 2) Détection explicite + logs clairs
#    - Cas A (monorepo): /repo/package.json contient "workspaces" ET dossier src/inventory-web présent
#    - Cas B (local app): pas de workspaces; on build dans le dossier courant
RUN set -eux; \
  echo "[detect] PWD=$(pwd)"; \
  if [ -f package.json ]; then echo "[detect] found package.json at /repo"; fi; \
  if [ -d src/inventory-web ]; then echo "[detect] found /repo/src/inventory-web"; fi; \
  if [ -f package.json ] && grep -q '"workspaces"' package.json && [ -d src/inventory-web ]; then \
    echo "[mode] MONOREPO"; \
    (npm ci --no-audit --no-fund || npm install --no-audit --no-fund); \
    npm -w src/inventory-web run build; \
    mkdir -p /out && mv src/inventory-web/dist /out/dist; \
  else \
    echo "[mode] LOCAL (single app)"; \
    (npm ci --no-audit --no-fund || npm install --no-audit --no-fund); \
    npm run build; \
    mkdir -p /out && mv dist /out/dist; \
  fi; \
  ls -la /out/dist

# ---------- RUNTIME ----------
FROM ${BASE_REG}/nginx:1.28-alpine
RUN apk add --no-cache openssl
COPY --from=build /out/dist /usr/share/nginx/html
# User-provided custom instructions
