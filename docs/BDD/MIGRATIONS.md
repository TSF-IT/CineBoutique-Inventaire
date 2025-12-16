# Migrations et exploitation BDD

## Stratégie
- **Source unique** : les migrations FluentMigrator de `src/inventory-infra/Migrations` pilotent le schéma. Toute évolution passe par une nouvelle classe nommée `yyyyMMdd_HHmmss_Description`.
- **Exécution au démarrage** : l’API applique les migrations si `APPLY_MIGRATIONS=true` et `DISABLE_MIGRATIONS=false`. En `Development`, `Docker`, `CI`, `Testing`, ces variables sont déjà positionnées pour appliquer automatiquement la baseline et le seed de démo.
- **Production** : aucune migration automatique par défaut. Forcer une montée de version via `APPLY_MIGRATIONS=true` (et laisser `DISABLE_MIGRATIONS=false`) puis revenir à `APPLY_MIGRATIONS=false` après réussite. Le seeding doit rester désactivé (`AppSettings__SeedOnStartup=false`).
- **Trace** : la table `public."VersionInfo"` suit la version courante. Ne pas la modifier manuellement.

## Lancer les migrations
### Dotnet local
```bash
# Connexion PostgreSQL obligatoire
export ConnectionStrings__Default='Host=localhost;Port=5432;Database=inventory;Username=postgres;Password=postgres'
export APPLY_MIGRATIONS=true
export DISABLE_MIGRATIONS=false
dotnet run --project src/inventory-api --no-build
```

### Docker Compose
```bash
# L’API démarre avec APPLY_MIGRATIONS=true et seed de démo
docker compose up --build
# Une fois la montée terminée, repassez APPLY_MIGRATIONS à false si besoin
```

### CI / GitHub Actions
- Le workflow `.github/workflows/ci.yml` provisionne PostgreSQL 16, exporte `ConnectionStrings__Default` et exécute `dotnet test` avec collecteur de couverture. Les migrations tournent lors du build/tests puisque `APPLY_MIGRATIONS=true` est fourni.

## Créer une nouvelle migration
1. Copier la convention `yyyyMMdd_HHmmss_Titre.cs` dans `src/inventory-infra/Migrations`.
2. Implémenter `Up`/`Down` et préférer les helpers FluentMigrator aux `Execute.Sql` sauf besoin spécifique.
3. Ajouter index/contraintes explicites (unicité, vérifications) pour refléter les invariants métier.
4. Couvrir le scénario par un test d’intégration (xUnit + Testcontainers) pour valider la migration et ses effets.

## Sécurité et seed
- Le seed (`InventoryDataSeeder`) injecte boutiques, zones et comptes de démonstration si `AppSettings__SeedOnStartup=true`. Ne pas l’activer en production.
- Les scripts SQL ad-hoc sont proscrits : toute modification doit être versionnée pour rester traçable.
