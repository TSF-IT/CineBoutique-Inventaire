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

Une stack Docker Compose est fournie pour orchestrer l'API ASP.NET Core, la base PostgreSQL et la PWA React servie par Nginx.

```bash
docker compose up --build -d
```

Avant de lancer la stack, tu peux valider la configuration Compose pour détecter toute erreur d'indentation ou de syntaxe :

```bash
docker compose config -q
```

Une fois les conteneurs démarrés :

- API : http://localhost:8080/swagger
- Front PWA : http://localhost:3000

Les migrations FluentMigrator et le seed de démonstration (zones `B1` à `B20`, `S1` à `S19` et 50 produits factices) sont exécutés automatiquement au démarrage lorsque `AppSettings:SeedOnStartup` vaut `true` (activé par défaut en environnement `Development`).

> 💡 Si un volume de données persiste d'une exécution précédente, créez manuellement la base :
>
> ```bash
> docker exec -it cineboutique-inventaire-db-1 psql -U postgres -d postgres -c "CREATE DATABASE cineboutique;"
> ```

Pour vérifier la santé et la connectivité de l'API :

```bash
curl http://localhost:8080/health
curl http://localhost:8080/ready
curl http://localhost:8080/api/locations
```

Les utilisateurs de test sont définis dans `src/inventory-api/appsettings.Development.json` :

- Alice — PIN `1111`
- Bob — PIN `2222`
- Charly — PIN `3333`
- Dana — PIN `4444`

L'endpoint `POST /auth/pin` retourne un JWT court si le PIN est valide. Les endpoints principaux actuellement exposés sont :

- `GET /health` : liveness simple.
- `GET /ready` : vérifie l'accès à PostgreSQL (`SELECT 1`).
- `GET /locations` : liste les zones d'inventaire et l'état d'occupation courant (filtrable par type de comptage).
- `GET /products/{code}` : recherche par SKU ou code EAN-8/EAN-13.
- `POST /auth/pin` : authentification par PIN/JWT (utilisateurs définis dans la configuration).
- `POST /api/inventories/{locationId}/restart` : clôture les runs actifs d'une zone pour redémarrer un comptage.

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

### Client web (React + Vite + TailwindCSS)

Installation des dépendances et exécution en mode développement :

```bash
cd src/inventory-web
npm install
npm run dev
```

Construction de la PWA et exécution de la suite de tests :

```bash
npm run build
npm run test
```

Fonctionnalités principales :

- PWA mobile-first avec manifest, service worker (vite-plugin-pwa) et icônes servies via CDN (personnalisables hors dépôt).
- Workflow guidé pour lancer un inventaire (sélection utilisateur → type → zone avec statut en temps réel → scan).
- Scan des codes-barres via caméra (getUserMedia + `@zxing/browser`) ou douchette Bluetooth simulée via champ de saisie.
- Gestion des produits hors référentiel (ajout manuel avec panneau coulissant).
- Espace administrateur protégé (login + CRUD des zones avec interactions swipe).
- Couverture de tests Vitest + Testing Library sur l'accueil, le workflow et la saisie par douchette.

### Ressources graphiques

Les icônes et logos ne sont plus versionnés afin de permettre l'export GitHub sans binaires. Référez-vous à `src/inventory-web/assets/README.md` et `src/inventory-web/public/assets/README.md` pour connaître l'emplacement attendu des visuels locaux et la procédure de personnalisation via CDN.

### Vérifications PostgreSQL en ligne de commande

Une fois connecté au conteneur PostgreSQL (`docker exec -it cineboutique-inventaire-db-1 psql -U postgres -d cineboutique`), les commandes suivantes permettent de valider l'état de la base :

- `\dx` pour vérifier que les extensions `uuid-ossp` et `pgcrypto` sont bien installées.
- `\dt` pour lister les tables créées par la migration (`Product`, `Location`, `InventorySession`, `CountingRun`, `CountLine`, `Conflict`, `Audit`).
- `SELECT COUNT(*) FROM "Location";` afin de confirmer la présence des 39 emplacements (`B1` à `B20`, `S1` à `S19`).

## Gestion centralisée des packages NuGet

La solution s'appuie sur la Central Package Management de .NET via le fichier `Directory.Packages.props` situé à la racine du dépôt. Toutes les dépendances NuGet partagent ainsi un catalogue de versions unique.

Pour vérifier les mises à jour disponibles, exécutez la commande suivante :

```bash
dotnet list package --outdated
```
