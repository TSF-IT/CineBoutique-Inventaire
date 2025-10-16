#!/usr/bin/env bash
set -Eeuo pipefail

# Aller à la racine du repo (ce fichier est à la racine)
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 1) S'assurer que dotnet est dispo (installe si nécessaire)
bash "$ROOT_DIR/scripts/bootstrap-dotnet.sh"

# 2) Restore & Build (solution entière)
dotnet restore "$ROOT_DIR/CineBoutique.Inventory.sln"
dotnet build   "$ROOT_DIR/CineBoutique.Inventory.sln" -c Release -v minimal

# 3) Tests + TRX + (option) couverture
OUTDIR="$ROOT_DIR/test-results/dotnet"
mkdir -p "$OUTDIR"
dotnet test "$ROOT_DIR/CineBoutique.Inventory.sln" \
  -c Release --no-build --logger "trx;LogFileName=tests.trx" \
  --results-directory "$OUTDIR"

echo "✔ Tests terminés. Résultats TRX → $OUTDIR/tests.trx"
