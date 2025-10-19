#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Unified test runner with 3 modes:
#  - CI (GitHub Actions): reuse the Postgres service (PGPORT provided)
#  - Local with Docker: start a local Postgres container
#  - Fallback (no Docker/no service): run only non-DB tests to keep Codex green
# -----------------------------------------------------------------------------

IN_CI="${GITHUB_ACTIONS:-}"
HAS_DOCKER=0
if command -v docker >/dev/null 2>&1 ; then HAS_DOCKER=1; fi

DB_HOST=127.0.0.1
DB_NAME=inventory
DB_USER=postgres
DB_PASS=postgres

CLEANUP=0
MODE=""
DB_PORT=""
CONTAINER_NAME=ci-postgres

if [[ -n "${IN_CI}" && -n "${PGPORT:-}" ]]; then
  MODE="ci"
  DB_PORT="${PGPORT}"
  echo "[run_tests] Mode=CI, using postgres service on port ${DB_PORT}"
elif [[ "${RUNTEST_NO_DB:-0}" == "1" ]]; then
  MODE="nodb"
  echo "[run_tests] Mode=NO-DB (forced by RUNTEST_NO_DB=1)"
elif [[ $HAS_DOCKER -eq 1 ]]; then
  MODE="docker"
  DB_PORT=5432
  echo "[run_tests] Mode=DOCKER, starting local postgres:${DB_PORT}..."
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  docker run -d --name "$CONTAINER_NAME" \
    -e POSTGRES_USER="$DB_USER" \
    -e POSTGRES_PASSWORD="$DB_PASS" \
    -e POSTGRES_DB="$DB_NAME" \
    -p "${DB_PORT}:5432" \
    postgres:16-alpine

  echo "[run_tests] Waiting for Postgres to become ready..."
  for i in {1..60}; do
    if docker exec "$CONTAINER_NAME" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done
else
  MODE="nodb"
  echo "[run_tests] Mode=NO-DB (no docker and no CI service available)"
fi

# -----------------------------------------------------------------------------
# Environment parity (same as CI)
# -----------------------------------------------------------------------------
export ASPNETCORE_ENVIRONMENT=CI
export DOTNET_ENVIRONMENT=CI
export AppSettings__SeedOnStartup=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1

if [[ "$MODE" != "nodb" ]]; then
  export ConnectionStrings__Default="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS"
  export TEST_DB_CONN="$ConnectionStrings__Default"
fi

# -----------------------------------------------------------------------------
# .NET restore/build
# -----------------------------------------------------------------------------
echo "[run_tests] dotnet --info"
dotnet --info

echo "[run_tests] restore"
dotnet restore --verbosity minimal

echo "[run_tests] build"
dotnet build --no-restore -c Release

# -----------------------------------------------------------------------------
# Tests (+ coverage)
# -----------------------------------------------------------------------------
rm -rf test-results/dotnet
mkdir -p test-results/dotnet

if [[ "$MODE" == "nodb" ]]; then
  echo "[run_tests] No-DB fallback: running only non-DB test projects"
  # Exclut l'assembly d'API (tests DB) pour que RunTest reste vert sans Postgres
  dotnet test --no-build -c Release \
    --filter "FullyQualifiedName!~CineBoutique.Inventory.Api.Tests" \
    --logger "trx;LogFileName=test.trx" \
    --results-directory "test-results/dotnet"
else
  echo "[run_tests] Full test suite with DB"
  dotnet test --no-build -c Release \
    --logger "trx;LogFileName=test.trx" \
    --results-directory "test-results/dotnet" \
    --collect:"XPlat Code Coverage;Format=cobertura"
fi

# Optional HTML coverage (only meaningful in DB mode)
if [[ "$MODE" != "nodb" ]] && command -v reportgenerator >/dev/null 2>&1; then
  reportgenerator \
    -reports:"test-results/dotnet/**/coverage.cobertura.xml" \
    -targetdir:"test-results/dotnet/coverage-report" \
    -reporttypes:HtmlInline;Cobertura
fi

echo "[run_tests] done."

# -----------------------------------------------------------------------------
# Cleanup local DB
# -----------------------------------------------------------------------------
if [[ "$MODE" == "docker" ]]; then
  echo "[run_tests] Stopping local Postgres..."
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
fi
