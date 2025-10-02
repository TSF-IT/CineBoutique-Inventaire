# Tests .NET pour Codex

## Commande principale

Exécuter :

```bash
make test-codex
```

Cette commande lance `scripts/test-codex.sh` qui gère automatiquement l'environnement .NET 8 et les dépendances nécessaires aux tests.

## Comportement selon la disponibilité de Docker

### 1. Docker disponible (scénario idéal)

- Construction d'une image SDK .NET 8 dédiée (`.codex/Dockerfile`).
- Montage du dépôt et du socket Docker hôte pour permettre à Testcontainers d'exécuter les tests d'API.
- Exécution complète de la solution (`CineBoutique.Inventory.sln`) : `restore`, `build` et `test` (API + Domain) en configuration Release.
- Les résultats de tests sont produits au format TRX (`test-results.trx`).

### 2. Docker indisponible

- Le script vérifie d'abord la présence d'un SDK .NET local (`dotnet --info`).
- Si le SDK est présent :
  - `dotnet restore`, `dotnet build` et `dotnet test` (projet `tests/inventory.domain.tests`) sont exécutés en configuration Release.
  - Les tests d'API dépendants de Docker sont volontairement ignorés.
- Si aucun SDK n'est détecté, le script tente un fallback via l'image SDK .NET 8 pour garantir l'exécution minimale des tests Domain.

## Résumé

- **Avec Docker** : couverture complète (Domain + API) grâce au conteneur SDK .NET 8 et à Testcontainers.
- **Sans Docker** : compilation et tests Domain uniquement, afin de prouver que le SDK .NET est disponible et fonctionnel malgré la limitation de l'environnement.

Ces scénarios évitent l'erreur « SDK .NET indisponible » et fournissent un retour fiable sur l'état de la solution, quel que soit l'environnement.

> ℹ️ Les tests d'API exécutent désormais `POST /api/auth/login` avec le compte administrateur de `CinéBoutique Paris`. Comme en environnement de développement, l'absence de secret (`Secret_Hash` nul) est autorisée en CI.
