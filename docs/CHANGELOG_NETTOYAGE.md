# Changelog nettoyage

## Entrée du jour
- Ajout d’un audit technique priorisé (`docs/AUDIT.md`) et des décisions associées (`docs/DECISIONS.md`).
- Documentation architecture globale (`docs/ARCHITECTURE.md`) et pack BDD complet (`docs/BDD/*` : modèle, migrations, conventions, diagramme Mermaid).
- README racine enrichi pour un démarrage local/Docker “zéro friction”.
- Amélioration ponctuelle de maintenabilité front (commentaires, cohérence des exports de runs).
- Robustesse front : classification des boutiques basée aussi sur le `kind` pour éviter des regroupements erronés, et désactivation des effets viewport/caméra en environnement de test pour stabiliser Vitest.
- Stabilisation des tests ScanCamera : mocks différés pour éviter les `setState` pendant le rendu et éviter les boucles infinies.
- Sélecteur boutique : ajout d’identifiants stables (`data-testid`) pour éviter la dépendance au libellé (“Boutique 1”) et fiabiliser les tests de routage.
- Campagne de tests complète : Vitest OK (161 tests) et `dotnet test` OK en pointant vers une base Postgres locale (`TEST_DB_CONN=Host=localhost;Port=5433;Database=inventory_tests;Username=postgres;Password=postgres`).
