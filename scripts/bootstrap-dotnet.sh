# scripts/bootstrap-dotnet.sh
#!/usr/bin/env bash
set -Eeuo pipefail

# Répertoires et variables d'env sûrs pour CI/agents
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

need_install=true
if command -v dotnet >/dev/null 2>&1; then
  if dotnet --list-sdks | grep -E '(^|[[:space:]])8\.' >/dev/null 2>&1; then
    need_install=false
  fi
fi

if [ "$need_install" = true ]; then
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  # Récupère le script officiel d’installation (Linux/macOS)
  if command -v curl >/dev/null 2>&1; then
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$tmp/dotnet-install.sh"
  else
    wget -qO "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
  fi
  chmod +x "$tmp/dotnet-install.sh"
  # Installe le canal LTS 8 dans $DOTNET_ROOT (sans sudo)
  "$tmp/dotnet-install.sh" --channel 8.0 --install-dir "$DOTNET_ROOT" --no-path
fi

echo "✔ dotnet available:"
dotnet --info
