# Conventions BDD

## Nommage et types
- Tables et colonnes en PascalCase pour coller au schéma existant (`Shop`, `ShopUser`, `InventorySession`…).
- Clés primaires : `uuid` avec défaut `uuid_generate_v4()` sauf cas particuliers (identité bigint pour `ProductGroup`, bigserial pour `audit_logs`).
- Horodatages : `DateTimeOffset` en UTC (`StartedAtUtc`, `CompletedAtUtc`, `CountedAtUtc`). Utiliser `SystemMethods.CurrentUTCDateTime` pour les valeurs par défaut.
- Booléens par défaut explicites (`IsAdmin=false`, `Disabled=false`, `IsResolved=false`) afin d’éviter les NULLs logiques.

## Indexation
- Index trigram + `immutable_unaccent(lower(...))` pour les recherches textuelles (`Product.Label`/`Name`/`Sku`/`Ean`).
- Index partiels pour les filtres fréquents : runs ouverts (`CompletedAtUtc IS NULL`), EAN non null, hash import.
- Contrainte d’unicité explicite quand l’invariant est métier (ex : `ShopId + LOWER(Login)`, `ShopId + LOWER(Sku)`, `uq_productimport_shopid`).

## Relations et intégrité
- Préférer `ON DELETE CASCADE` pour les entités enfants strictes (`Product.ShopId`, `ProductImport.ShopId`), et `ON DELETE SET NULL` pour les relations optionnelles (`Product.GroupId`, `ProductGroup.ParentId`).
- Les sessions d’inventaire n’embarquent pas le `ShopId` : toute nouvelle fonctionnalité doit continuer de vérifier la cohérence via `Location.ShopId` ou ajouter un champ dédié si un cloisonnement multi-boutique devient nécessaire.

## Audit et historique
- Utiliser `Audit` pour les événements métier structurés (payload JSON), et `audit_logs` pour la trace technique ou les jobs. Préférer un `category` stable (ex: `inventory.run`, `import.catalog`).
- En cas d’ajout d’actions sensibles (résolution de conflit, purge catalogue), logguer l’acteur (`actor`) et la décision prise.

## Nouvelles colonnes / migrations
- Garder les valeurs par défaut explicites et non nullables quand l’invariant le permet.
- Ajouter systématiquement une migration de nettoyage des données existantes si une contrainte d’unicité ou de non-null est introduite.
- Tester les migrations via Testcontainers (voir `tests/inventory.api.tests`) pour garantir la compatibilité montante.
