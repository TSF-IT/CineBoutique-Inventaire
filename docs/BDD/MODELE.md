# Modèle de données inventaire

## Vue d’ensemble
Base PostgreSQL unique, gérée par FluentMigrator, centrée sur les boutiques (`Shop`), leurs zones (`Location`), les utilisateurs magasin (`ShopUser`), le catalogue (`Product`, `ProductGroup`) et le cycle d’inventaire (`InventorySession`, `CountingRun`, `CountLine`, `Conflict`). Les imports catalogue sont tracés dans `ProductImport` et `ProductImportHistory`; l’audit technique/functional est assuré par `Audit` et `audit_logs`.

## Tables principales
- **Shop** : `Id` (uuid, PK, défaut `uuid_generate_v4()`), `Name` (256, unique insensible à la casse), `Kind` (`boutique`/`lumiere`/`camera`, default `boutique`). Index sur `Kind` et contrainte de valeur autorisée.
- **ShopUser** : `Id` (uuid, PK), `ShopId` (FK → Shop), `Login` (unique par boutique, lower-cased), `DisplayName` (unique par boutique), `IsAdmin` (bool, default false), `Secret_Hash` (hash mot de passe, default chaîne vide), `Disabled` (bool). Index combinés sur `ShopId + DisplayName` et `ShopId + LOWER(Login)`.
- **ProductGroup** : `Id` (bigint, identité, PK), `Code` (text, unique), `Label` (text, not null), `ParentId` (self-FK set null). Index trigram sur `Label` pour la recherche.
- **Product** : `Id` (uuid, PK), `ShopId` (FK cascade → Shop), `Sku` (32, unique par boutique), `Name` (256), `Ean` (64, nullable), `CodeDigits` (64, nullable), `Attributes` (jsonb, défaut `{}`), `GroupId` (FK set null → ProductGroup), `CreatedAtUtc` (datetimeoffset). Index nombreux : trigram sur `Sku`/`Ean`/`Name` (unaccent), GIN sur `Attributes`, doublon unique et index simple sur `ShopId + LOWER(Sku)`, index sur `Ean`, `CodeDigits`.
- **Location** : `Id` (uuid, PK), `ShopId` (FK → Shop), `Code` (32, unique par boutique sur `UPPER(Code)`), `Label` (128), `Disabled` (bool). Index sur `ShopId + Code`. Seed de zones B1–B20 et S1–S19 pour la boutique “Cinéboutique Saint-Denis”.
- **InventorySession** : `Id` (uuid, PK), `Name` (256), `StartedAtUtc` (datetimeoffset), `CompletedAtUtc` (nullable). Pas de FK vers Shop : l’app s’appuie sur le lien Location → Shop pour contextualiser.
- **CountingRun** : `Id` (uuid, PK), `InventorySessionId` (FK → InventorySession), `LocationId` (FK → Location), `CountType` (smallint, default 1), `StartedAtUtc`, `CompletedAtUtc` (nullable), `OperatorDisplayName` (200, default “Unknown”), `OwnerUserId` (FK nullable → ShopUser). Index `IX_CountingRun_Location_CountType_Open` pour les runs ouverts, index sur `OwnerUserId`, contrainte unique des runs actifs (`InventorySessionId`,`LocationId`,`CountType`,`OperatorDisplayName` quand `CompletedAtUtc` est null).
- **CountLine** : `Id` (uuid, PK), `CountingRunId` (FK → CountingRun), `ProductId` (FK → Product), `Quantity` (decimal(18,3)), `CountedAtUtc`.
- **Conflict** : `Id` (uuid, PK), `CountLineId` (FK → CountLine), `Status` (64), `Notes` (1024, nullable), `CreatedAtUtc`, `ResolvedAtUtc` (nullable), `ResolvedQuantity` (int, nullable), `IsResolved` (bool, default false). Ajouté par migration `20261105_090000_AddConflictResolutionColumns`.
- **Audit** : `Id` (uuid, PK), `EntityName` (256), `EntityId` (128), `EventType` (64), `Payload` (jsonb), `CreatedAtUtc`; index sur `(EntityName, EntityId)`.
- **audit_logs** : `id` (bigserial, PK), `at` (datetimeoffset, default now), `actor` (nullable), `message` (text, not null), `category` (200, nullable).
- **ProductImport** : `Id` (uuid, PK), `ShopId` (FK cascade → Shop), `FileName` (text), `FileHashSha256` (char(64)), `RowCount` (int), `ImportedAtUtc` (datetimeoffset, default now). Contrainte unique `uq_productimport_shopid` (un import en cours par boutique) + index unique `ux_productimport_shopid_filehash` (déduplication par hash).
- **ProductImportHistory** : `Id` (uuid, PK), `ShopId` (FK → Shop), `StartedAt`, `FinishedAt` (nullable), `Username` (nullable), `FileSha256` (128, nullable), `TotalLines`/`Inserted`/`ErrorCount` (int, défaut 0), `Status` (32), `DurationMs` (nullable). Index sur `StartedAt` (desc), `FileSha256`, `ShopId`.

## Points d’attention
- Seeds boutiques/zones insérées conditionnellement (idempotent). Ne pas compter dessus en production : prévoir un jeu de données réel ou désactiver `AppSettings__SeedOnStartup`.
- `InventorySession` n’embarque pas de `ShopId` : l’intégrité métier repose sur la cohérence `CountingRun.LocationId → Location.ShopId`.
- Les recherches produit s’appuient sur des index trigram + unaccent ; les requêtes doivent utiliser `LOWER`/`immutable_unaccent` pour bénéficier des index.
