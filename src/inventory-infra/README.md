# Infrastructure d'inventaire

## Comment lancer les migrations (Docker)

Les migrations FluentMigrator sont exécutées automatiquement lorsque le service `api` démarre. Pour les déclencher dans un environnement Docker :

1. Assurez-vous que le volume PostgreSQL est prêt :
   ```bash
   docker compose up -d db
   ```
2. Lance ensuite le service API (il appliquera les migrations avant d'exposer les endpoints) :
   ```bash
   docker compose up --build api
   ```
3. Une fois les migrations appliquées et les logs `Now listening on:` visibles, vous pouvez laisser l'API tourner ou interrompre le conteneur avec `Ctrl+C`. Pour arrêter et nettoyer les ressources créées uniquement pour la migration, exécutez :
   ```bash
   docker compose down
   ```

> ℹ️ Les migrations sont idempotentes : relancer le conteneur appliquera uniquement les nouvelles migrations.
