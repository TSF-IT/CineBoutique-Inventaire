#!/usr/bin/env bash
set -euo pipefail

# Répertoire d'installation sur la VM (à adapter si besoin)
APP_DIR="${APP_DIR:-$HOME/apps/CineBoutique-Inventaire}"
BRANCH="${BRANCH:-deploy}"

echo "[deploy] Working dir: ${APP_DIR}"
cd "$APP_DIR"

# S'assure que la branche locale est 'deploy' et suit origin/deploy
if [ ! -d .git ]; then
  echo "[deploy] ERROR: .git not found in $APP_DIR"
  exit 1
fi

echo "[deploy] Fetching..."
git fetch origin "$BRANCH"

echo "[deploy] Checking out $BRANCH..."
git checkout "$BRANCH"

echo "[deploy] Reset to origin/$BRANCH..."
git reset --hard "origin/$BRANCH"

echo "[deploy] Pulling submodules if any..."
git submodule update --init --recursive

echo "[deploy] Bringing containers up (build & recreate if needed)..."
docker compose down --remove-orphans || true
docker compose up --build -d

echo "[deploy] Pruning old images (optional)..."
docker image prune -f || true

echo "[deploy] Done."
