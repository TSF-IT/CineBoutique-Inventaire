#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SLN_PATH="$ROOT_DIR/CineBoutique.Inventory.sln"
TEST_PROJECT_PATH="$ROOT_DIR/tests/inventory.domain.tests"
DOTNET_ROOT="$ROOT_DIR/.dotnet"
DOTNET_INSTALL_SCRIPT="$DOTNET_ROOT/dotnet-install.sh"
SDK_VERSION="${SDK_VERSION:-}"

log() {
  echo "[codex] $*"
}

parse_sdk_version() {
  if [[ -n "$SDK_VERSION" ]]; then
    return
  fi

  if [[ -f "$ROOT_DIR/global.json" ]]; then
    local parsed_version
    parsed_version=$(grep -m1 -oE '"version"\s*:\s*"([^"]+)"' "$ROOT_DIR/global.json" | sed 's/.*"//;s/"$//') || true
    if [[ -n "$parsed_version" ]]; then
      SDK_VERSION="$parsed_version"
      return
    fi
  fi

  SDK_VERSION="8.0.100"
}

install_local_sdk() {
  mkdir -p "$DOTNET_ROOT"

  if [[ ! -f "$DOTNET_INSTALL_SCRIPT" ]]; then
    log "Téléchargement du script d'installation du SDK .NET…"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_INSTALL_SCRIPT"
    chmod +x "$DOTNET_INSTALL_SCRIPT"
  fi

  log "Installation du SDK .NET $SDK_VERSION dans $DOTNET_ROOT…"
  if ! "$DOTNET_INSTALL_SCRIPT" --version "$SDK_VERSION" --install-dir "$DOTNET_ROOT" --no-path; then
    local major minor channel
    IFS='.' read -r major minor _ <<< "$SDK_VERSION"
    channel="$major.$minor"
    log "Version $SDK_VERSION indisponible, tentative avec le canal $channel.x…"
    "$DOTNET_INSTALL_SCRIPT" --channel "$channel" --install-dir "$DOTNET_ROOT" --no-path
  fi

  export DOTNET_ROOT
  export PATH="$DOTNET_ROOT:$PATH"
}

ensure_local_sdk() {
  parse_sdk_version

  if command -v dotnet >/dev/null 2>&1; then
    local current_version
    current_version=$(dotnet --version 2>/dev/null || echo "")
    if [[ -n "$current_version" ]]; then
      local current_major="${current_version%%.*}"
      local required_major="${SDK_VERSION%%.*}"
      if [[ "$current_major" == "$required_major" ]]; then
        log "SDK .NET détecté localement ($current_version)."
        return
      fi
      log "SDK .NET détecté ($current_version) mais incompatible, installation de la version $SDK_VERSION…"
    fi
  else
    log "Aucun SDK .NET détecté, installation de la version $SDK_VERSION…"
  fi

  install_local_sdk
}

run_tests() {
  log "Vérification du SDK .NET…"
  dotnet --info

  log "Restore de la solution…"
  dotnet restore "$SLN_PATH"

  log "Build Release de la solution…"
  dotnet build "$SLN_PATH" -c Release --no-restore

  log "Exécution des tests Domain…"
  dotnet test "$TEST_PROJECT_PATH" -c Release --no-build --logger "trx;LogFileName=test-results.trx"
}

main() {
  ensure_local_sdk
  run_tests
  log "Tests Domain terminés avec succès."
}

main "$@"
