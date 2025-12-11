# Modèle de données CineBoutique-Inventaire (PostgreSQL)

## Généralités
- Extensions requises : `uuid-ossp`, `pgcrypto`, `pg_trgm`, `unaccent` (fonction `immutable_unaccent` utilisée pour les index trigram).
- Toutes les évolutions passent par FluentMigrator (`inventory-infra/Migrations`). Deux migrations actives : schéma initial (20260101_000001) et ajouts de colonnes de résolution de conflit (20261105_090000).
- Les valeurs `uuid` sont générées côté base (`uuid_generate_v4()`), colonnes booléennes initialisées à `false`, JSONB pour les attributs produit.

## Tables principales
- **Shop** (`Id`, `Name`, `Kind`) : boutiques/entités. Contrainte de domaine sur `Kind` (`boutique`, `lumiere`, `camera`). Index unique sur `LOWER(Name)`, index sur `Kind`.
- **ShopUser** (`Id`, `ShopId`, `Login`, `DisplayName`, `IsAdmin`, `Secret_Hash`, `Disabled`) : utilisateurs par boutique. Clé étrangère vers `Shop`. Unicité par boutique sur `DisplayName` et sur `LOWER(Login)`. Index `ShopId + DisplayName`.
- **ProductGroup** (`Id` identity, `Code`, `Label`, `ParentId`) : hiérarchie facultative de familles produit. Contrainte unique sur `Code`, index sur `ParentId`, index trigram sur `Label` (via `immutable_unaccent`).
- **Product** (`Id`, `ShopId`, `Sku`, `Name`, `Ean`, `CodeDigits`, `Attributes` jsonb, `GroupId`, `CreatedAtUtc`) : catalogue par boutique. FKs vers `Shop` (cascade) et `ProductGroup` (SET NULL). Unicité `(ShopId, LOWER(Sku))`. Index sur `Sku`, `Ean` (normal + lower), `CodeDigits`.
- **Location** (`Id`, `ShopId`, `Code`, `Label`, `Disabled`) : zones de comptage. FK vers `Shop`. Unicité `(ShopId, UPPER(Code))`, index `ShopId + Code`.
- **InventorySession** (`Id`, `ShopId`, `StartedAtUtc`, `CompletedAtUtc`, `ResetRequestedAtUtc`, `ResetCompletedAtUtc`) : session d’inventaire par boutique. FK vers `Shop`. Index sur `ShopId` et sur `CompletedAtUtc`.
- **CountingRun** (`Id`, `InventorySessionId`, `LocationId`, `CountType`, `OwnerUserId`, `OperatorDisplayName`, `StartedAtUtc`, `CompletedAtUtc`) : run de comptage par zone/type. FKs vers `InventorySession` et `Location`. Index sur `LocationId`, `InventorySessionId`, `CountType`.
- **CountLine** (`Id` identity, `CountingRunId`, `Ean`, `Quantity`, `IsManual`, `CreatedAtUtc`) : lignes de comptage liées à un run. FK vers `CountingRun` (cascade). Index `CountingRunId`, index sur `Ean`.
- **Conflict** (`Id` identity, `InventorySessionId`, `LocationId`, `Ean`, `Quantity1`, `Quantity2`, `ResolvedQuantity`, `IsResolved`, `CreatedAtUtc`, `ResolvedAtUtc`, `ResolvedBy`) : divergences entre passages. FKs vers `InventorySession` et `Location`. Colonnes ajoutées par migration 20261105 : `ResolvedQuantity`, `IsResolved` (rétro-calculées si `ResolvedAtUtc` non nul).
- **Audit** (`Id` identity, `SessionId`, `LocationId`, `RunId`, `Kind`, `Payload`, `CreatedAtUtc`) : traces fonctionnelles. Index `CreatedAtUtc`.
- **audit_logs** (`id`, `timestamp`, `message`, `username`, `event`) : journalisation textuelle utilisée par `DbAuditLogger`.
- **ProductImport** (`Id`, `ShopId`, `Sha1`, `FileName`, `ImportedAtUtc`, `Total`, `Inserted`, `Skipped`, `ErrorCount`, `IsDryRun`, `Errors`, `DurationMs`) : statut de l’import en cours (clé sur `ShopId + Sha1` pour idempotence).
- **ProductImportHistory** (mêmes colonnes que `ProductImport` + `EndedAtUtc`) : historique des imports terminés.

## Relations clés
- `Shop` 1─* `ShopUser`, `Location`, `Product`, `InventorySession`.
- `InventorySession` 1─* `CountingRun`, `Conflict`.
- `CountingRun` 1─* `CountLine`.
- `ProductGroup` gère une relation hiérarchique (FK sur `ParentId` avec `SET NULL`).
- Audit/audit_logs ne sont pas reliées par contraintes FK pour rester légères.

## Règles et contraintes métier
- Codes zones uniques par boutique (majuscules) et désactivables (`Disabled`). La désactivation forcée purge les `CountLine` puis supprime les `CountingRun` orphelins dans une transaction.
- Runs ouverts : un run par `LocationId` et `CountType` peut rester actif (`CompletedAtUtc` nul). Les endpoints exposent le statut par type (1er, 2ᵉ, contrôle) et l’opérateur (`OwnerUserId`/`OperatorDisplayName`).
- Conflits : stockent les quantités des passages 1 et 2; les colonnes `ResolvedQuantity`/`IsResolved` permettent de suivre l’arbitrage.
- Catalogue : SKU et EAN uniques par boutique (sensibles à la casse pour EAN, insensibles pour SKU), `CodeDigits` pour recherche partielle. Attributs libres en JSONB.
- Import produits : idempotent via `Sha1` du fichier, verrouillage en mémoire durant l’exécution, table d’historique pour traçabilité.

## Points de vigilance
- Pas de contraintes directes empêchant les doublons `CountLine` sur un même run → à traiter côté service si besoin.
- Le verrou d’import est en mémoire (`InMemoryImportLockService`) : en cluster, prévoir un verrou partagé (table dédiée ou lock distribué).
- Les audits texte (`audit_logs`) ne portent pas de FK : injecter le contexte (`ShopId`, `LocationId`, utilisateur) dans le message pour faciliter les investigations.
- Indexation : le volume de `CountLine` peut croître rapidement; surveiller la fragmentation des index `CountingRunId`/`Ean`, ajouter des index couvrants si des requêtes analytiques apparaissent.
