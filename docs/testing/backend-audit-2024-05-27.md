# Audit des tests backend — 27/05/2024

| Test | Statut | Décision | Justification |
| --- | --- | --- | --- |
| AuditLoggingTests | Obsolète | supprimer | Couvrage basé sur l'ancien logger maison, remplacé par Serilog et audité via les nouveaux flux HTTP ; vérification déplacée dans les tests d'intégration métier. |
| CompletedRunDetailEndpointTests | Obsolète | supprimer | Endpoint `/api/inventories/runs/{id}` supprimé lors de la refonte des résumés, le test ne correspondait plus au contrat API actuel. |
| ConflictZoneDetailEndpointTests | Obsolète | supprimer | Les attentes sur l'ancien schéma des conflits (colonnes `Operator`) étaient périmées ; remplacé par un scénario bout en bout dans `InventoryCountingFlowTests`. |
| CountingRunOwnershipTests | Obsolète | supprimer | S'appuyait sur la logique historique `operatorName` supprimée ; le nouveau flux propriétaire est validé via `InventoryCountingFlowTests`. |
| DtoSerializationTests | Obsolète | supprimer | Tests purement réflexifs sans valeur métier ; la sérialisation JSON est désormais couverte par les tests HTTP. |
| InventoryCompletionEndpointTests | Obsolète | supprimer | Endpoint renommé et enrichi (boucles de concordance) — ancien test ne vérifiait plus les statuts ni la résolution de conflits. |
| InventoryRunLifecycleTests | Obsolète | supprimer | Basé sur des fixtures semi-mockées et sur Dapper ; remplacé par des tests HTTP réalistes avec Postgres. |
| InventorySummaryEndpointTests | Obsolète | supprimer | Requêtes SQL et schémas de résumé ont changé ; le test se basait sur des seeds inexistants. |
| LocationSeedingTests | Obsolète | supprimer | Vérifiait des scripts de seeds legacy supprimés au profit du seeder runtime. |
| LocationsEndpointTests | Obsolète | supprimer | Ancienne route `/api/locations` et projection dépassée ; la couverture passe par les scénarios d'intégration (`InventoryCountingFlowTests`). |
| ShopUserSeedingTests | Obsolète | supprimer | Couplé à des seeds supprimés et non représentatifs des flux CRUD actuels. |
| ShopUsersEndpointTests | Obsolète | supprimer | Contrats (DTO, statuts) modifiés après refonte ; remplacé par `ShopUserCrudTests`. |
| ShopsEndpointTests | Obsolète | supprimer | Ancien modèle sans audits ni validations ; remplacé par `ShopCrudTests`. |
| Infrastructure/SoftOperatorMiddlewareTests | Obsolète | supprimer | Testait des détails internes du middleware alors que le flux opérateur est maintenant couvert via les endpoints HTTP. |
| Endpoints/EndpointUtilitiesTests | Obsolète | supprimer | Tests unitaires sur helpers internes (formatage) sans validation des contrats API. |
| UnitTest1 | Obsolète | supprimer | Fichier de squelette xUnit inutilisé. |
| InventoryCountingFlowTests | Nouveau | garder | Vérifie la boucle de comptage jusqu'à concordance, la détection des conflits et la restitution des statuts de zones via `/api/conflicts` et `/locations`. |
| ShopCrudTests | Nouveau | garder | Couvre le CRUD HTTP des boutiques avec validation des statuts et du JSON retourné. |
| ShopUserCrudTests | Nouveau | garder | Vérifie l'ensemble des opérations utilisateurs (création, mise à jour, désactivation) avec contrôles métier. |
| ProductEndpointsTests | Nouveau | garder | Valide la création produit, la recherche par SKU/EAN et les cas d'erreur (400/409). |

## Synthèse
- Les anciens tests mélangeaient mocks Dapper, seeds obsolètes et schémas retirés : ils ne validaient plus le contrat HTTP.
- Les nouveaux tests reposent sur `WebApplicationFactory` + Postgres via Testcontainers, ce qui garantit la cohérence avec la configuration réelle.
- Les scénarios critiques (boucles de concordance, conflits, CRUD boutiques/utilisateurs/produits) sont maintenant couverts de bout en bout.
- L'exécution peut être désactivée explicitement via `CI_SKIP_DOCKER_TESTS`; si Docker est indisponible, les tests sont marqués en "skipped" pour rendre l'état explicite.
