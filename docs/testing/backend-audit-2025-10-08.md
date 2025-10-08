# Audit des tests backend – 2025-10-08

## Objectifs et plan
- Cartographier la couverture de tests par endpoint et par règle métier critique.
- Remplacer les assertions dépendantes de Dapper par des tests de contrat Minimal API via `WebApplicationFactory`.
- Exécuter tous les scénarios d’inventaire contre PostgreSQL (Testcontainers) en appliquant les migrations existantes.
- Introduire des builders de données réutilisables pour des fixtures cohérentes et isolées.
- Vérifier les règles de boucle "jusqu’à concordance" et la résolution automatique des conflits.

## Hypothèses
- Les schémas issus des migrations les plus récentes sont appliqués pendant les tests.
- Les endpoints REST publics constituent la surface à valider (pas de vérification des couches internes).
- Les environnements CI utilisent déjà les images Postgres 16-alpine et peuvent supporter Testcontainers.

## Choix techniques
- Base `InventoryApiTestBase` partagée pour piloter `WebApplicationFactory`, gérer la connexion unique et nettoyer la base.
- `InventoryTestDataBuilder` centralise la création des entités (Shop, Location, Session, CountingRun, CountLine, Conflict…).
- Toutes les assertions se font via des appels HTTP et sérialisations DTO afin de coller aux contrats JSON réels.
- Les tests d’intégration s’exécutent sur Postgres grâce au container de test fourni par Testcontainers.

## Couverture par endpoint
- `POST /api/inventories/{locationId}/complete`
  - validation des erreurs (400, 404).
  - création d’un comptage + lignes et récupération via `GET /api/inventories/runs/{id}`.
  - mise à jour d’un run existant démarré via `POST /start`.
  - génération de conflits lorsqu’un C1 diffère d’un C2.
  - résolution automatique lors d’une boucle supplémentaire alignée sur un comptage précédent.
- `GET /api/inventories/runs/{runId}`
  - 404 lorsque le run n’existe pas ou n’est pas clôturé.
  - réponse complète avec lignes, métadonnées et opérateur.
- `GET /api/inventories/summary`
  - cas vide, run actif, runs terminés et zones en conflit.
  - affichage des propriétaires (`OwnerDisplayName`) avec et sans `OwnerUserId`.
- `GET /api/conflicts/{locationId}`
  - 404 sur location inconnue.
  - détail des écarts (runs concernés, quantités C1/C2, delta).
- `POST /api/inventories/{locationId}/start`
  - démarrage nominal, détection de conflit utilisateur et contrôle de périmètre magasin.
- `POST /api/inventories/{locationId}/release`
  - libération d’un run actif avec vérification de l’état de la zone.
- `POST /api/inventories/{locationId}/restart`
  - clôture forcée des runs actifs et redémarrage possible immédiat.

## Règles métier couvertes
- Démarrage unique par couple (zone, type de comptage, opérateur) et protection contre les conflits.
- Création automatique des produits inconnus et agrégation des quantités lors d’une complétion.
- Gestion des conflits C1/C2 et persistance dans la table `Conflict`.
- Boucle "jusqu’à concordance" : suppression des conflits après un comptage supplémentaire aligné.
- Restitution des propriétaires de comptage, en tenant compte des colonnes optionnelles (`OwnerUserId`, `OperatorDisplayName`).

## Stratégie de données de test
- Builders orientés scénario injectés via `InventoryTestDataBuilder` pour éviter la duplication SQL.
- Nettoyage systématique des tables cibles entre tests (`DatabaseCleaner`).
- Horodatages cohérents pour contrôler les champs `StartedAtUtc`, `CompletedAtUtc` et l’ordre des runs.

## Scénarios clés validés
1. Complétion d’un comptage avec création de run et restitution contractuelle.
2. Cycle complet Start → Release et Start → Restart.
3. Génération et résolution d’écarts multi-comptages.
4. Synthèse inventaire (sessions actives, runs terminés, zones en conflit).
5. Détails de run complété avec vérification des lignes, quantités et opérateurs.

## Prochaines étapes suggérées
- Étendre les builders aux tests `Locations` et `Audit` restants pour supprimer les dépendances directes à Dapper.
- Introduire une factory de scénarios (p.ex. inventaire complet avec plusieurs produits) pour réduire davantage la duplication.
- Ajouter des tests de résilience (timeouts, annulation) autour des endpoints critiques si nécessaire.
- Intégrer les nouveaux scénarios dans le workflow CI afin d’exécuter la suite complète sur chaque PR.
