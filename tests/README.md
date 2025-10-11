# Tests – Exécution locale

## Objectif
Documenter l’exécution des tests backend (xUnit) en local, avec ou sans Docker, et rappeler les mécanismes d’isolation de la base de données.

## Aperçu rapide
- **Par défaut** : les tests d’intégration utilisent Testcontainers pour démarrer Postgres.
- **Fallback** : en l’absence de Docker, pointez vers une instance Postgres existante via `TEST_DB_CONN` (ex. Postgres local).
- La fixture `InventoryApiFixture` isole désormais les suites sur un schéma dédié lorsque `TEST_DB_CONN` est défini.

## Pré-requis
- .NET 8 SDK.
- Docker Desktop/Engine **ou** une base Postgres accessible localement (UTF-8, extensions par défaut).

## 1. Avec Docker Desktop (recommandé)
1. Vérifiez que Docker est démarré.
2. Exécutez :
   ```bash
   dotnet test CineBoutique.Inventory.sln -c Release
   ```
   Testcontainers provisionne automatiquement un conteneur Postgres `postgres:16-alpine`.

Aucun paramètre supplémentaire n’est requis : la variable `TEST_DB_CONN` doit rester vide.

## 2. Sans Docker – Postgres local
### 2.1 Préparer la base
1. Créez une base `inventory` et un compte `postgres/postgres` (ou adaptez la chaîne de connexion).
2. Ouvrez les ports réseau si nécessaire (par défaut `127.0.0.1:5432`).

### 2.2 Utiliser le fichier `runsettings`
- Un profil prêt à l’emploi est fourni : [`tests/local.runsettings`](./local.runsettings).
- Il expose les variables d’environnement suivantes :
  - `TEST_DB_CONN` → chaîne de connexion Postgres locale.
  - `ASPNETCORE_ENVIRONMENT` et `DOTNET_ENVIRONMENT` → `Development` pour reproduire la configuration locale.

#### Depuis la CLI
```bash
dotnet test CineBoutique.Inventory.sln -c Release --settings tests/local.runsettings
```

#### Depuis Visual Studio
1. Menu `Test` → `Configure Run Settings` → `Select Solution-wide runsettings`.
2. Choisissez `tests/local.runsettings`.
3. Lancez les tests (`Test Explorer`).

> 📸 La capture d’écran « Select Solution-wide runsettings » est disponible sur le SharePoint interne (référence : QA/VisualStudio/SelectRunsettings.png). Elle n’est pas ajoutée au dépôt conformément à la politique « pas d’assets binaires ».

## Isolation des tests
- Sans `TEST_DB_CONN`, chaque suite s’exécute dans une base éphémère issue de Testcontainers.
- Avec `TEST_DB_CONN`, `InventoryApiFixture` génère un schéma dédié par exécution (`it_<guid>`), le crée avant les migrations puis le détruit/recrée avant chaque scénario (`DbResetAsync`).
- `ResetAndSeedAsync` continue de vider le schéma et d’appliquer les migrations avant de ré-injecter les données de test.

## Rappel des variables disponibles
| Variable | Effet | Valeur par défaut |
|----------|-------|-------------------|
| `TEST_DB_CONN` | Désactive Testcontainers et force l’utilisation de la chaîne fournie. | *(vide)* |
| `CI_SKIP_DOCKER_TESTS` | Skip explicite des tests d’intégration (CI lente). | *(vide)* |
| `ASPNETCORE_ENVIRONMENT` | Environnement ASP.NET Core forcé par les fixtures. | `Testing` (override par runsettings) |
| `DOTNET_ENVIRONMENT` | Alias pour .NET Generic Host. | `Testing` (override par runsettings) |

## Bonnes pratiques
- Ne jamais exécuter les tests sur une base de production.
- Nettoyer régulièrement la base locale si vous l’utilisez pour d’autres scénarios.
- Documenter toute variation de chaîne de connexion dans ce fichier afin de garder une source unique de vérité.
