# Modélisation des données

Ce document synthétise la structure actuelle de la base PostgreSQL gérée par les migrations FluentMigrator du projet `inventory-infra`.

## Modèle Conceptuel de Données (MCD)

```mermaid
erDiagram
    SHOP ||--o{ LOCATION : "possède"
    SHOP ||--o{ SHOP_USER : "emploie"
    LOCATION ||--o{ COUNTING_RUN : "accueille"
    INVENTORY_SESSION ||--o{ COUNTING_RUN : "planifie"
    COUNTING_RUN ||--o{ COUNT_LINE : "enregistre"
    PRODUCT ||--o{ COUNT_LINE : "concerne"
    COUNT_LINE ||--o{ CONFLICT : "peut générer"
```

- **Shop** : boutique physique (Paris, Bordeaux, Montpellier, Marseille, Bruxelles).
- **Location** : zone physique de stockage (codes `B1` à `B20`, `S1` à `S19`) rattachée à une boutique.
- **ShopUser** : compte utilisateur rattaché à une boutique pour l'usage en magasin.
- **InventorySession** : campagne d'inventaire regroupant plusieurs comptages.
- **CountingRun** : passage de comptage effectué sur une zone donnée.
- **CountLine** : quantité relevée pour un produit dans un run.
- **Product** : référence commerciale identifiée par SKU/EAN.
- **Conflict** : différentiel entre deux comptages d'une même zone.
- **Audit** et **audit_logs** : tables d'historisation techniques non reliées par clé étrangère.

## Modèle Physique de Données (MPD)

```mermaid
erDiagram
    SHOP {
        GUID Id PK
        STRING(256) Name
    }
    SHOP_USER {
        GUID Id PK
        GUID ShopId FK
        STRING(128) Login
        STRING(256) DisplayName
        BOOL IsAdmin
        STRING(512) Secret_Hash
        BOOL Disabled
    }
    PRODUCT {
        GUID Id PK
        STRING(32) Sku
        STRING(256) Name
        STRING(13) Ean
        DATETIMEOFFSET CreatedAtUtc
    }
    LOCATION {
        GUID Id PK
        STRING(32) Code
        STRING(128) Label
        GUID ShopId FK
    }
    INVENTORY_SESSION {
        GUID Id PK
        STRING(256) Name
        DATETIMEOFFSET StartedAtUtc
        DATETIMEOFFSET CompletedAtUtc
    }
    COUNTING_RUN {
        GUID Id PK
        GUID InventorySessionId FK
        GUID LocationId FK
        DATETIMEOFFSET StartedAtUtc
        DATETIMEOFFSET CompletedAtUtc
        INT16 CountType
        STRING(200) OperatorDisplayName
    }
    COUNT_LINE {
        GUID Id PK
        GUID CountingRunId FK
        GUID ProductId FK
        DECIMAL_P18S3 Quantity
        DATETIMEOFFSET CountedAtUtc
    }
    CONFLICT {
        GUID Id PK
        GUID CountLineId FK
        STRING(64) Status
        STRING(1024) Notes
        DATETIMEOFFSET CreatedAtUtc
        DATETIMEOFFSET ResolvedAtUtc
    }
    AUDIT {
        GUID Id PK
        STRING(256) EntityName
        STRING(128) EntityId
        STRING(64) EventType
        JSONB Payload
        DATETIMEOFFSET CreatedAtUtc
    }
    AUDIT_LOG {
        INT64 Id PK
        DATETIMEOFFSET At
        STRING(320) Actor
        TEXT Message
        STRING(200) Category
    }

    SHOP ||--o{ LOCATION : "FK"
    INVENTORY_SESSION ||--o{ COUNTING_RUN : "FK"
    LOCATION ||--o{ COUNTING_RUN : "FK"
    COUNTING_RUN ||--o{ COUNT_LINE : "FK"
    PRODUCT ||--o{ COUNT_LINE : "FK"
    COUNT_LINE ||--o{ CONFLICT : "FK"
    SHOP ||--o{ SHOP_USER : "FK"
```

> ℹ️ `DECIMAL_P18S3` correspond à une colonne `DECIMAL(18,3)` dans PostgreSQL. La notation a été ajustée pour rester compatible avec Mermaid.

## Synthèse des contraintes

| Table | Clés principales | Index / Contraintes notables |
| --- | --- | --- |
| `Shop` | `Id` | Index unique `UQ_Shop_LowerName` sur `LOWER(Name)`. |
| `ShopUser` | `Id` | Index unique `UQ_ShopUser_Shop_LowerLogin` sur (`ShopId`, `LOWER(Login)`). |
| `Product` | `Id` | Index uniques sur `Sku` et `Ean`. |
| `Location` | `Id` | Index unique `UQ_Location_Shop_Code` (`ShopId`, `UPPER(Code)`). |
| `InventorySession` | `Id` | — |
| `CountingRun` | `Id` | Index partiel `IX_CountingRun_Location_CountType_Open`, index unique `ux_countingrun_active_triplet`. |
| `CountLine` | `Id` | FK vers `CountingRun` et `Product`. |
| `Conflict` | `Id` | FK vers `CountLine`. |
| `Audit` | `Id` | Index composé `IX_Audit_Entity` (`EntityName`, `EntityId`). |
| `audit_logs` | `id` | Table annexe pour la journalisation technique. |

## Seed disponible

- 5 boutiques (`CinéBoutique Paris`, `CinéBoutique Bordeaux`, `CinéBoutique Montpellier`, `CinéBoutique Marseille`, `CinéBoutique Bruxelles`).
- 39 zones (`B1` à `B20`, `S1` à `S19`) rattachées par défaut à `CinéBoutique Paris`, créées via `InventoryDataSeeder` et/ou la migration `202404010002_SeedLocations` complétée par la migration `202410010001_AddShopTableAndLocationShopRelation`.
- Comptes utilisateurs de démonstration injectés par `InventoryDataSeeder` : un administrateur par boutique (`login=administrateur`) et des comptes "Utilisateur n" (Paris : 5 comptes supplémentaires, autres boutiques : 4).
- 5 utilisateurs de démonstration (`appsettings.Development.json`) authentifiés par PIN.
- Aucun produit ni comptage n'est injecté par défaut : toute donnée métier supplémentaire doit être créée via l'API ou des scripts dédiés.

Ces représentations visuelles peuvent être rendues directement dans GitHub grâce au support de Mermaid.
