#!/usr/bin/env bash
set -euo pipefail

# -----------------------------------------------------------------------------
# Run tests locally or in CI with the SAME behavior (DB + env + coverage)
# -----------------------------------------------------------------------------

# Are we in GitHub Actions with a Postgres service already provisioned?
IN_CI="${GITHUB_ACTIONS:-}"
PGPORT_ENV="${PGPORT:-}"

DB_HOST=127.0.0.1
DB_NAME=cineboutique
DB_USER=postgres
DB_PASS=postgres

CLEANUP=0
CONTAINER_NAME=ci-postgres

if [[ -n "$IN_CI" && -n "$PGPORT_ENV" ]]; then
  # CI job: a postgres service is already running and mapped on $PGPORT
  DB_PORT="$PGPORT_ENV"
  echo "[run_tests] Using CI Postgres service on port $DB_PORT"
else
  # Local: start our own postgres 16-alpine
  DB_PORT=5432
  CLEANUP=1
  echo "[run_tests] Starting local Postgres on port $DB_PORT..."
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
  docker run -d --name "$CONTAINER_NAME" \
    -e POSTGRES_USER="$DB_USER" \
    -e POSTGRES_PASSWORD="$DB_PASS" \
    -e POSTGRES_DB="$DB_NAME" \
    -p "$DB_PORT:5432" \
    postgres:16-alpine

  echo "[run_tests] Waiting for Postgres to become ready..."
  for i in {1..60}; do
    if docker exec "$CONTAINER_NAME" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done
fi

# -----------------------------------------------------------------------------
# Environment parity with CI
# -----------------------------------------------------------------------------
export ASPNETCORE_ENVIRONMENT=CI
export DOTNET_ENVIRONMENT=CI
export AppSettings__SeedOnStartup=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1

export ConnectionStrings__Default="Host=$DB_HOST;Port=$DB_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASS"
export TEST_DB_CONN="$ConnectionStrings__Default"

# -----------------------------------------------------------------------------
# .NET build + tests + coverage (identical to CI)
# -----------------------------------------------------------------------------
echo "[run_tests] dotnet --info"
dotnet --info

echo "[run_tests] restore"
dotnet restore --verbosity minimal

echo "[run_tests] build"
dotnet build --no-restore -c Release

echo "[run_tests] test"
rm -rf test-results/dotnet
mkdir -p test-results/dotnet

dotnet test --no-build -c Release \
  --logger "trx;LogFileName=test.trx" \
  --results-directory "test-results/dotnet" \
  --collect:"XPlat Code Coverage;Format=cobertura"

# Optional HTML coverage report if reportgenerator is installed
if command -v reportgenerator >/dev/null 2>&1; then
  reportgenerator \
    -reports:"test-results/dotnet/**/coverage.cobertura.xml" \
    -targetdir:"test-results/dotnet/coverage-report" \
    -reporttypes:HtmlInline;Cobertura
fi

echo "[run_tests] done."

# -----------------------------------------------------------------------------
# Cleanup local DB
# -----------------------------------------------------------------------------
if [[ "$CLEANUP" == "1" ]]; then
  echo "[run_tests] Stopping local Postgres..."
  docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
fi
