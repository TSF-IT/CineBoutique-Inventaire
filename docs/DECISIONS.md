# Décisions prises lors de cette passe

## D1 – Normalisation de la documentation
- **Problème** : absence de référentiel à jour (architecture, BDD, runbook migrations) compliquait l’onboarding et l’audit.
- **Décision** : ajouter `docs/ARCHITECTURE.md`, `docs/BDD/*`, `docs/AUDIT.md` et `docs/CHANGELOG_NETTOYAGE.md` en français comme source de vérité.
- **Alternatives** : répartir les infos dans les README existants ou conserver les docs fragmentées dans `docs/database`. Rejeté pour manque de traçabilité.
- **Conséquences** : les nouvelles contributions devront mettre à jour ces fichiers ; le périmètre des migrations et de l’archi est désormais documenté et diffable.

## D2 – Authentification : laisser AdminHeader pour le dev, documenter le verrouillage prod
- **Problème** : l’API utilise par défaut le schéma AdminHeader, pratique en dev mais dangereux si laissé en production.
- **Décision** : ne pas modifier le code pour préserver les environnements existants, mais documenter explicitement le verrouillage prod (désactiver AdminHeader, exiger Authority/Audience) dans l’audit et le README.
- **Alternatives** : forcer un fail-fast immédiat en production. Rejeté pour éviter une rupture sans concertation.
- **Conséquences** : action P0 ajoutée dans l’audit ; nécessite un ticket dédié pour implémenter le fail-fast.

## D3 – Évolutions CI différées pour le front
- **Problème** : les tests Vitest/Playwright ne sont pas exécutés en CI.
- **Décision** : ne pas modifier les workflows dans cette passe (scope doc), mais inscrire l’action P0 dans `docs/AUDIT.md` et décrire les commandes dans le README.
- **Alternatives** : activer immédiatement les tests front dans `.github/workflows/ci.yml`. Rejeté pour limiter le risque de rupture pipeline sans validation préalable.
- **Conséquences** : dépend d’une itération ultérieure ; les équipes disposent des commandes exactes pour l’ajout futur.
