# Audit technique – CinéBoutique Inventaire

## Périmètre observé
- Monorepo .NET 8 + React : API minimal ASP.NET Core (`src/inventory-api`), infrastructure Dapper/FluentMigrator (`src/inventory-infra`), domaine quasi vide (`src/inventory-domain`), contrats partagés (`src/inventory-shared`), PWA React 19/Vite (`src/inventory-web`).
- Base de données PostgreSQL pilotée par FluentMigrator (migrations `20260101_000001_InitialSchema` et `20261105_090000_AddConflictResolutionColumns`), seeding d’exemple via `InventoryDataSeeder`.
- Tests : forte couverture d’intégration API (xUnit + Testcontainers), domaine limité à un smoke test, Vitest/Testing Library et suites Playwright côté front.
- CI GitHub Actions (`.github/workflows/ci.yml` + lint/sécurité dédiés) : build/test .NET, typecheck + build web, artefacts de couverture, mais sans exécution des tests front.

## Observations clés (tech debt / risques)
- **Couche métier dispersée** : la logique d’inventaire (sessions, comptages, résolutions) vit dans `SessionRepository`/`RunRepository` et les endpoints, la bibliothèque domaine est quasi vide. Conséquence : invariants difficiles à isoler et à tester unitairement, couplage fort API/infra.
- **Garde-fous prod perfectibles** : l’authentification header admin est active par défaut (`Authentication:UseAdminHeader=true`) et les seeds peuvent tourner si `AppSettings__SeedOnStartup` reste à true. Aucun fail-fast n’empêche ce mode en Production, ce qui expose à une configuration laxiste.
- **Tests front non exécutés en CI** : la pipeline CI build et typecheck le front mais n’exécute ni Vitest ni Playwright. Les tests existent (`src/inventory-web/tests/*`) et les régressions UI/API contractuelles peuvent passer inaperçues.
- **Imports catalogue sensibles au blocage** : `ProductImport` impose un enregistrement unique par boutique (`uq_productimport_shopid`) et un hash unique par fichier. Un import échoué ou non purgé peut bloquer les suivants (absence de job de purge/relance automatique).
- **Outils locaux hétérogènes** : `run_tests.sh` propose des modes avec/ sans Docker, mais peut ignorer les tests dépendants de la base si Docker absent. Risque de faux positifs en exécution locale.

## Actions priorisées
- **P0 – Sécurisation config prod** (effort : faible, risque : faible)  
  - Bloquer `Authentication:UseAdminHeader=true` quand `ASPNETCORE_ENVIRONMENT=Production` et exiger `Authentication:Authority/Audience`.  
  - Documenter un runbook seed/migrations (fait dans `docs/BDD/MIGRATIONS.md`) et exiger `AppSettings__SeedOnStartup=false` en prod.
- **P0 – Étendre la CI** (effort : moyen, risque : faible)  
  - Ajouter `npm -w src/inventory-web run test` et au moins un smoke Playwright sur `/` ou `/runs` dans `.github/workflows/ci.yml`.  
  - Garder les artefacts (JUnit/coverage) pour corrélation avec le back.
- **P1 – Structurer le domaine** (effort : moyen/fort, risque : moyen)  
  - Extraire la logique de sessions/comptages dans des services/domain objects testables (statuts, transitions, résolution de conflits).  
  - Introduire des tests unitaires ciblés sur ces invariants sans passer par l’API.
- **P1 – Fiabiliser les imports** (effort : moyen, risque : moyen)  
  - Ajouter un état “failed/expired” et une tâche de purge/relance pour `ProductImport` / `ProductImportHistory`.  
  - Journaliser les erreurs d’import dans `audit_logs` pour traçabilité opérateur.
- **P2 – Hygiène des outils locaux** (effort : faible, risque : faible)  
  - Faire échouer `run_tests.sh` si les tests base ne tournent pas (option `--require-docker` ou variable explicite).  
  - Documenter le mode attendu dans README pour éviter les faux positifs.
