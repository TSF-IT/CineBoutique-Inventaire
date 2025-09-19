# CinéBoutique - Inventaire

Ce dépôt monorepo regroupe l'ensemble des composants nécessaires à la future application d'inventaire CinéBoutique.

## Structure actuelle

- `src/inventory-api` : projet ASP.NET Core minimal API.
- `src/inventory-domain` : bibliothèque pour les règles métier.
- `src/inventory-infra` : infrastructure et accès aux données.
- `src/inventory-shared` : contrats et DTO partagés.
- `src/inventory-web` : client PWA React (Vite + TypeScript).
- `tests/*` : projets de tests automatisés associés.
- `build/ci.yml` : pipeline GitHub Actions (à enrichir).

Chaque projet .NET cible .NET 8 et applique des analyzers configurés en avertissements bloquants.

## Prochaines étapes

Les étapes suivantes ajouteront progressivement le modèle de données, l'API complète, le client PWA et l'intégration Docker/CI.
