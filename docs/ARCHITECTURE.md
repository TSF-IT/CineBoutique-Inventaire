# Architecture applicative

## Vue d’ensemble
- **API** : ASP.NET Core 8 (minimal API) dans `src/inventory-api`, exposant les endpoints inventaire/produits/boutiques. Authentification par header admin ou JWT, middlewares de diagnostic et d’observabilité (Serilog + OpenTelemetry).
- **Infrastructure** : `src/inventory-infra` fournit l’accès PostgreSQL via Dapper, les migrations FluentMigrator et le seeding (`InventoryDataSeeder`). Les repositories `SessionRepository`/`RunRepository` encapsulent les transactions et SQL métiers.
- **Domaine** : `src/inventory-domain` contient les contrats d’audit (faible volumétrie). Les invariants métier sont aujourd’hui dans l’infra/API.
- **Front** : PWA React 19 + Vite dans `src/inventory-web`, composants Tailwind, routing client, appels HTTP typés (`src/app/api`), contextes pour l’état utilisateur/sessions.
- **Contrats partagés** : `src/inventory-shared` (assembly marker) pour factoriser d’éventuels DTO si besoin.

## Flux principaux
1. **Comptage d’inventaire**  
   PWA → endpoints `/api/runs/*` (Features/Inventory). L’API contrôle l’ouverture de run (unicité par zone/type), persiste `CountingRun` et les `CountLine`, détecte les conflits. Les exports utilisent des vues agrégées dans les repositories.
2. **Catalogue**  
   PWA → endpoints `/api/products/*` (Features/Products). Recherche trigram/unaccent dans PostgreSQL, import catalogue via `ProductImport*` avec journalisation d’historique.
3. **Administration**  
   CRUD boutiques/utilisateurs/zones via endpoints `ShopsEndpoints`/`ShopUsersEndpoints`/`LocationsEndpoints`. Authentification header admin en dev, JWT en prod.

## Backend (API/Infra)
- **Configuration** : `Program.cs` active Serilog (sauf `DISABLE_SERILOG=true`), configure CORS (`AllowDev`, `PublicApi`), prometheus `/metrics`, health `/health`/`/ready` et diagnostics `/api/_diag/*`.
- **Middlewares** : pipeline minimal API, filtre `RequireOperatorHeadersFilter` pour imposer les entêtes opérateur en écriture, middleware d’auth (AdminHeader ou JWT).
- **Données** : Dapper sur un `NpgsqlConnection` fourni par `IDbConnectionFactory`. Transactions explicites dans les méthodes critiques (démarrage/fin de run, reset inventaire). Validation métier dans les repositories (vérification de zone, utilisateur, doublons).
- **Migrations** : FluentMigrator + runner PostgreSQL, voir `docs/BDD/MIGRATIONS.md`.
- **Tests** : `tests/inventory.api.tests` (xUnit + Testcontainers) couvrent les flux API majeurs (runs, produits, locations, reset). Domaine réduit à un smoke test.

## Frontend (PWA)
- **Structure** : `src/app` organisé par domaines (pages, components, hooks, contexts). UI Tailwind 4, composants maison (`Button`, `ErrorPanel`, `LoadingIndicator`, modales).
- **API client** : `src/app/api/inventoryApi.ts` encapsule les appels, gère les erreurs HTTP typées.
- **Fonctionnel** : sélection de boutique/utilisateur, assistance au scan (`@zxing` + `BarcodeDetector`), gestion des runs en cours, exports CSV (`CompletedRunsModal`).
- **Tests** : Vitest + Testing Library pour les composants clés (`tests/unit`), Playwright (`tests/e2e`) pour les parcours scan. Non exécutés en CI actuellement.

## Observabilité & CI/CD
- Logs Serilog (console, format compact), métriques Prometheus, healthchecks prêts pour Kubernetes ou Docker Compose.
- CI GitHub Actions : build+test .NET, typecheck+build front, génération de couverture Cobertura. Pipelines lint (ESLint) et audit sécurité (npm audit) séparés.
- Docker Compose (`docker-compose.yml`) : Postgres 16, API, front Nginx ; migrations et seed auto en dev/Docker.

## Conventions de contribution
- **C#** : nullable activé, analyzers .NET, warnings bloquants. Respecter les patterns existants (minimal API, repositories Dapper). Nommer les migrations `yyyyMMdd_HHmmss_Description`.
- **TypeScript** : `strict` activé, hooks avec préfixe `use`, composants fonctionnels. Préférer les utilitaires dans `src/app/utils` ou `lib`.
- **Docs** : toute nouvelle fonctionnalité doit être décrite en français et référencée depuis `docs/DECISIONS.md` si une décision architecturale est prise.
- **Tests** : viser un test unitaire ou d’intégration pour tout flux métier ajouté. Ajouter les commandes correspondantes dans la CI le cas échéant.
