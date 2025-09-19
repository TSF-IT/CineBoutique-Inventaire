# Infrastructure d'inventaire

## Comment lancer les migrations (Docker)

Les migrations FluentMigrator sont exécutées automatiquement lorsque le service `api` démarre. Pour les déclencher dans un environnement Docker :

1. Assure-toi que le volume PostgreSQL est prêt :
   ```bash
   docker compose up -d db
   ```
2. Lance ensuite le service API (il appliquera les migrations avant d'exposer les endpoints) :
   ```bash
   docker compose up --build api
   ```
3. Une fois les migrations appliquées et les logs `Now listening on:` visibles, tu peux laisser l'API tourner ou interrompre le conteneur avec `Ctrl+C`. Pour arrêter et nettoyer les ressources créées uniquement pour la migration, exécute :
   ```bash
   docker compose down
   ```

> ℹ️ Les migrations sont idempotentes : relancer le conteneur appliquera uniquement les nouvelles migrations.
