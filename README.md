# CinéBoutique - Inventaire

Ce dépôt monorepo regroupe l'ensemble des composants nécessaires à la future application d'inventaire CinéBoutique.

## Structure

- `src/inventory-api` : projet ASP.NET Core minimal API exposant les endpoints d'inventaire et gérant l'authentification par PIN/JWT.
- `src/inventory-infra` : infrastructure et accès aux données (FluentMigrator, Dapper, seeding de démonstration).
- `src/inventory-domain` : bibliothèque pour les règles métier.
- `src/inventory-shared` : contrats et DTO partagés.
- `src/inventory-web` : client PWA React (Vite + TypeScript).
- `tests/*` : projets de tests automatisés associés.
- `build/ci.yml` : pipeline GitHub Actions (à enrichir).

Chaque projet .NET cible .NET 8 et applique des analyzers configurés en avertissements bloquants.

## Démarrage rapide en local

Une stack Docker Compose est fournie pour orchestrer l'API et PostgreSQL. La base de données utilisée est nommée `cineboutique`.

```bash
docker compose build --no-cache
docker compose up
```

Si un volume de données persiste d'une exécution précédente, créez manuellement la base :

```bash
docker exec -it cineboutique-inventaire-db-1 psql -U postgres -d postgres -c "CREATE DATABASE cineboutique;"
```

Dans un second terminal, vérifiez la santé et la connectivité de l'API :

```bash
curl http://localhost:8080/health
curl http://localhost:8080/ready
curl http://localhost:8080/locations
```

Les migrations FluentMigrator et le seed de démonstration (zones `B1` à `B20`, `S1` à `S19` et 50 produits factices) sont exécutés automatiquement au démarrage lorsque `AppSettings:SeedOnStartup` vaut `true` (activé par défaut en environnement `Development`). Les utilisateurs de test sont définis dans `src/inventory-api/appsettings.Development.json` :

- Alice — PIN `1111`
- Bob — PIN `2222`
- Charly — PIN `3333`
- Dana — PIN `4444`

L'endpoint `POST /auth/pin` retourne un JWT court si le PIN est valide. Les endpoints principaux actuellement exposés sont :

- `GET /health` : liveness simple.
- `GET /ready` : vérifie l'accès à PostgreSQL (`SELECT 1`).
- `GET /locations` : liste les zones d'inventaire seedées.
- `GET /products/{code}` : recherche par SKU ou code EAN-8/EAN-13.
- `POST /auth/pin` : authentification par PIN/JWT (utilisateurs définis dans la configuration).

## Configuration applicative

- Chaîne de connexion PostgreSQL : `ConnectionStrings:Default` (surchargée dans Docker via variable d'environnement `ConnectionStrings__Default`).
- Paramètres généraux : `AppSettings` (ex. `SeedOnStartup`).
- Authentification : `Authentication` (issuer, audience, secret JWT, durée, utilisateurs PIN).

## Scripts utiles

Pour exécuter la solution en local hors conteneur :

```bash
dotnet restore
dotnet build
```

Les migrations sont exécutées automatiquement au démarrage de l'API.

## Gestion centralisée des packages NuGet

La solution s'appuie sur la Central Package Management de .NET via le fichier `Directory.Packages.props` situé à la racine du dépôt. Toutes les dépendances NuGet partagent ainsi un catalogue de versions unique.

Pour vérifier les mises à jour disponibles, exécutez la commande suivante :

```bash
dotnet list package --outdated
```
