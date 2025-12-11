# Vue d’ensemble CineBoutique-Inventaire

## Architecture
- Backend : API minimale .NET 8 (C#) avec Dapper + FluentMigrator, audit via `DbAuditLogger`, horodatage `IClock`, middlewares de corrélation et de garde (`AppTokenGuardMiddleware`, `SoftOperatorMiddleware`). Authentification configurable (en-têtes Admin/AppToken par défaut, JWT possible). Observabilité : Serilog ou console, OpenTelemetry + endpoint Prometheus `/metrics`, healthchecks `/health` et `/ready`.
- Infrastructure : dépôt `inventory-infra` (factories Npgsql, dépôts Dapper pour produits, runs, sessions), migrations versionnées (`Migrations/20260101_000001_InitialSchema` + `20261105_090000_AddConflictResolutionColumns`), seeds de démo (`InventoryDataSeeder`).
- Frontend : PWA React (Vite + TypeScript) mobile-first, routes dans `src/inventory-web/src/app/AppRoutes.tsx`, hooks/context (`InventoryContext`, `ShopContext`), services HTTP typés (`src/app/api/*`, `src/lib/api/http.ts`), composants métiers (scanner, import CSV, résolution de conflits).
- Base de données : PostgreSQL avec extensions `uuid-ossp`, `pgcrypto`, `pg_trgm`, `unaccent`; schéma géré uniquement par FluentMigrator, indices trigram sur libellés produits/groupes, contraintes d’unicité (codes zones par boutique, SKU/EAN par boutique).
- Conteneurisation : Dockerfiles pour API et web, `docker-compose*.yml` pour orchestrer Postgres + API + PWA (Nginx), flags `APPLY_MIGRATIONS`/`DISABLE_MIGRATIONS`/`SeedOnStartup`.

## Flux métiers principaux
- **Onboarding front** : sélection de boutique (`/select-shop`), utilisateur, puis entrée dans le workflow d’inventaire.
- **Gestion des zones (locations)** : listing/statuts (`GET /api/locations`), création/mise à jour/désactivation, audit systématique en français.
- **Sessions et runs d’inventaire** : ouverture (`StartInventoryRunHandler`), reprise/relance (`RestartInventoryRunHandler`), finalisation (`CompleteInventoryRunHandler`), libération (`ReleaseInventoryRunHandler`), réinitialisation boutique (`ResetShopInventoryHandler`).
- **Lignes de comptage** : `CountLine` associées aux runs (`CountingRun`) et aux sessions (`InventorySession`), détection de conflits (`ConflictsEndpoints`), rapports (`ReportsEndpoints`).
- **Catalogue produits** : recherche/lookup/suggestions (Dapper + indices trigram), import CSV administrateur (`ImportEndpoints` + `ProductImportService`), gestion des groupes et attributs JSONB.
- **Administration** : gestion des boutiques et utilisateurs (`ShopsEndpoints`, `ShopUsersEndpoints`), endpoints admin produits, exposition Swagger (dev/test), diagnostics `/api/_diag/*`.

## Modules backend clefs
- `Features/Inventory/*` : endpoints zones, runs, sessions, conflits, rapports.
- `Features/Products/*` : catalogue, administration, import.
- `Features/Shops/*` : boutiques + utilisateurs.
- `Infrastructure/Database/*` : dépôts Dapper (produits, inventaire), utilitaires SQL pour opérateurs, verrous d’import.
- `Infrastructure/Auditing` : `DapperAuditLogger` + pont domaine `DomainAuditBridgeLogger`.
- `Infrastructure/Seeding` : données de démonstration (boutiques, zones, produits).
- Configuration/Hosting : `Program.cs`, CORS (`AllowDev`, `PublicApi`), Swagger/OpenAPI, Prometheus, middlewares de sécurité/legacy opérateurs.

## Modules frontend clefs
- Pages : sélection boutique/utilisateur, tableau de bord `HomePage`, assistant inventaire (`InventoryLocationStep` → `InventoryCountTypeStep` → `InventorySessionPage`/`ScanCameraPage`), résolution de conflits, import produits, écrans admin (zones/utilisateurs).
- Services API : `src/app/api/inventoryApi.ts` (inventaire), `adminApi.ts`, `shopUsers.ts`, `inventoryApi.test.ts` (contrats de test).
- États/contexts : `InventoryContext`, `ShopContext`, hooks utilitaires (`useAsync`, `useScanDuplicateFeedback`, `useCamera`…).
- UI/UX : composants dédiés (scanner code-barres, modales de runs, cartes produits, panneau d’import), messages utilisateurs en français, tests Vitest/RTL sur workflow clé.

## Points sensibles et recommandations
- **Transactions/purge zones** : la désactivation forcée purge les `CountLine` et `CountingRun` associés dans une transaction Npgsql explicite. Bien valider le paramètre `force` côté UI pour éviter une suppression involontaire.
- **Types de comptage** : les valeurs attendues sont 1 (1er passage), 2 (2ᵉ), 3 (contrôle). Les requêtes `GET /api/locations` filtrent et agrègent les runs ouverts/terminés pour chaque type détecté.
- **Concurrence sur runs** : les requêtes Dapper utilisent `EnsureConnectionOpenAsync`; penser aux verrous applicatifs (middleware `SoftOperatorMiddleware`) pour éviter des runs concurrents sur la même zone.
- **Imports produits** : verrouillage en mémoire (`InMemoryImportLockService`) seulement; à renforcer si plusieurs instances API.
- **Sécurité** : endpoints publics dépendants de `AppSettings:AdminEndpointsPublic`/`CatalogEndpointsPublic`. En production, privilégier JWT + rôles `Admin`, limiter les origines CORS et protéger `/metrics`.
- **Observabilité** : corrélation par header, logs en français, métriques Prometheus pour lookup/import. Ajouter des événements métier (ex : erreurs d’import, purges forcées) si besoin de traçabilité avancée.
