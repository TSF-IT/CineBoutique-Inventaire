#!/usr/bin/env bash
set -euo pipefail

SLN="CineBoutique.Inventory.sln"

# Détecte Docker (nécessaire aux API tests avec Testcontainers)
has_docker() {
  command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1
}

run_full_suite_in_container() {
  docker build -t cineboutique-dotnet-sdk:8 .codex
  docker run --rm \
    -v "$(pwd):/workspace" \
    -v /var/run/docker.sock:/var/run/docker.sock \
    -w /workspace \
    cineboutique-dotnet-sdk:8 \
    bash -lc "
      dotnet --info && \
      dotnet restore \"$SLN\" && \
      dotnet build   \"$SLN\" -c Release --no-restore && \
      dotnet test    \"$SLN\" -c Release --no-build --logger 'trx;LogFileName=test-results.trx'
    "
}

run_domain_suite_in_container() {
  docker build -t cineboutique-dotnet-sdk:8 .codex
  docker run --rm \
    -v "$(pwd):/workspace" \
    -w /workspace \
    cineboutique-dotnet-sdk:8 \
    bash -lc "
      dotnet --info && \
      dotnet restore \"$SLN\" && \
      dotnet build   \"$SLN\" -c Release --no-restore && \
      dotnet test    tests/inventory.domain.tests -c Release --no-build --logger 'trx;LogFileName=test-results.trx'
    "
}

# 3A. Si Docker dispo -> on lance tout via conteneur SDK + socket Docker
if has_docker; then
  echo "[codex] Docker détecté : exécution complète (API + Domain) dans un conteneur SDK .NET 8…"
  run_full_suite_in_container
  exit $?
fi

# 3B. Si Docker indisponible -> au moins prouver que le SDK .NET s’exécute et valider build + tests sans Testcontainers
echo '[codex] Docker indisponible : on effectue restore/build + tests *sans* API (Domain uniquement).'
if dotnet --info; then
  dotnet restore "$SLN"
  dotnet build   "$SLN" -c Release --no-restore
  dotnet test tests/inventory.domain.tests -c Release --no-build --logger "trx;LogFileName=test-results.trx"
  exit $?
fi

echo "[codex] SDK .NET introuvable : fallback Docker léger…"
if command -v docker >/dev/null 2>&1; then
  run_domain_suite_in_container
  exit $?
fi

echo "[codex] Aucun SDK .NET local ni Docker disponible pour exécuter les tests." >&2
exit 1
