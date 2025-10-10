# Backend – Base de données de tests

## Objectif
Garantir l’exécution hermétique de la suite backend (.NET 8) en isolant une base Postgres dédiée aux tests d’intégration.

## Option A : Testcontainers (par défaut)
- Le fixture partagé `PostgresContainerFixture` démarre automatiquement un conteneur `postgres:16-alpine`.
- Aucun Postgres local n’est requis : Docker doit simplement être disponible.
- Les migrations FluentMigrator sont exécutées explicitement par `InventoryApiFixture` avant chaque scénario.
- Le schéma est réinitialisé via `DbResetAsync()` entre les tests pour garantir l’isolation.

### Pré-requis locaux
1. Docker Desktop / Docker Engine fonctionnel.
2. .NET 8 SDK installé.

### Commandes utiles
```bash
# Lancer l’ensemble des tests backend (unitaires + intégration)
dotnet test CineBoutique.Inventory.sln -c Release

# Relancer uniquement les tests API
dotnet test tests/inventory.api.tests -c Release
```

## Option B : Service Postgres en CI (fallback)
Si l’environnement CI ne peut pas exécuter Docker, utilisez un service Postgres classique. Un bloc YAML commenté est fourni dans `.github/workflows/ci.yml` :

```yaml
# services:
#   postgres:
#     image: postgres:16-alpine
#     ports:
#       - 5432:5432
#     env:
#       POSTGRES_DB: inventory_tests
#       POSTGRES_USER: postgres
#       POSTGRES_PASSWORD: postgres
#     options: >-
#       --health-cmd="pg_isready -U postgres"
#       --health-interval=10s
#       --health-timeout=5s
#       --health-retries=5
```

Pointez alors les tests vers cette instance via `TEST_DB_CONN` (alias `TEST_DB_CONNECTION`, voir ci-dessous).

> ℹ️ **Workflows CI** : la suite backend est exécutée automatiquement sur chaque `push`, `pull request`, `merge_group` et lancement manuel (`workflow_dispatch`). Les PR dépourvues du workflow sur la branche cible déclenchent malgré tout l’exécution via l’évènement `push` (un simple commit supplémentaire suffit).

## Variables d’environnement
| Variable | Description | Valeur par défaut |
|----------|-------------|-------------------|
| `TEST_DB_CONN` / `TEST_DB_CONNECTION` | Chaîne de connexion explicite pour réutiliser une base Postgres existante (désactive Testcontainers). | Vide → Testcontainers démarre une DB éphémère |
| `CI_SKIP_DOCKER_TESTS` | Forcer le skip des tests d’intégration quand Docker est indisponible. | Vide |
| `ASPNETCORE_ENVIRONMENT` | Fixé à `Testing` par les fixtures pour désactiver les migrations automatiques au démarrage. | `Testing` pendant les tests |

## Conseils
- Ne jamais cibler `localhost:5432` en dur : utilisez toujours la chaîne fournie par `InventoryApiFixture`.
- Pour diagnostiquer un échec de conteneur, lancez `docker ps -a` et `docker logs` localement.
- Les tests peuvent être redirigés vers une base locale existante en positionnant `TEST_DB_CONN` (ou `TEST_DB_CONNECTION`) avant `dotnet test`.
