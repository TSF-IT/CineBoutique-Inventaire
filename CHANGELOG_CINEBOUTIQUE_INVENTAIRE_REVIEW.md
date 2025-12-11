# Revue du 2025-12-11

## Backend
- Harmonisation des validations des zones d’inventaire : paramètre `countType` accepte explicitement 1/2/3 (1er/2ᵉ passage, contrôle) avec réponse 400 normalisée en français.
- Centralisation des erreurs 400/404/409 dans `LocationsEndpoints` (helpers réutilisables) et correction des messages/audits avec accents corrects (création, mise à jour, désactivation).

## Frontend
- Traduction complète du message d’erreur HTTP 415 (type de contenu) dans `inventoryApi` pour rester cohérent avec l’IHM francophone.
- Nettoyage automatique de la DOM de test (`setupTests.ts`) pour éviter l’empilement d’instances React entre scénarios Vitest et fiabiliser les sélecteurs accessibles (notamment le champ de scan). 

## Documentation
- Ajout de `DOCS/CineBoutique-Inventaire-Overview.md` (architecture, flux principaux, modules, points sensibles).
- Ajout de `DOCS/BaseDeDonnees-CineBoutique-Inventaire.md` (schéma PostgreSQL, relations, contraintes et vigilances).
