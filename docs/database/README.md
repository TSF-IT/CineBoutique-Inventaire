# Modélisation des données

Ce document synthétise la structure actuelle de la base PostgreSQL gérée par les migrations FluentMigrator du projet `inventory-infra`.

## Modèle Conceptuel de Données (MCD)

```mermaid
erDiagram
    SHOP ||--o{ LOCATION : "regroupe"
    SHOP ||--o{ SHOP_USER : "emploie"
    SHOP_USER ||..o{ COUNTING_RUN : "propriétaire"
    LOCATION ||--o{ COUNTING_RUN : "accueille"
    INVENTORY_SESSION ||--o{ COUNTING_RUN : "planifie"
    COUNTING_RUN ||--o{ COUNT_LINE : "enregistre"
    PRODUCT ||--o{ COUNT_LINE : "concerne"
    COUNT_LINE ||--o{ CONFLICT : "peut générer"
```

- **Shop** : boutique CinéBoutique (Paris, Bordeaux, Montpellier, Marseille, Bruxelles).
- **ShopUser** : compte utilisateur lié à une boutique (administrateur ou opérateur, secret haché Argon2/bcrypt).
- **Location** : zone physique de stockage (codes `B1` à `B20`, `S1` à `S19`).
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
        DATETIMEOFFSET CreatedAtUtc
    }
    SHOP_USER {
        GUID Id PK
        GUID ShopId FK
        STRING(128) Login
        STRING(128) DisplayName
        BOOLEAN IsAdmin
        STRING(512) Secret_Hash
        BOOLEAN Disabled
        DATETIMEOFFSET CreatedAtUtc
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
        GUID ShopId FK
        STRING(32) Code
        STRING(128) Label
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
        GUID OwnerUserId FK
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
    SHOP ||--o{ SHOP_USER : "FK"
    SHOP_USER ||..o{ COUNTING_RUN : "FK"
    INVENTORY_SESSION ||--o{ COUNTING_RUN : "FK"
    LOCATION ||--o{ COUNTING_RUN : "FK"
    COUNTING_RUN ||--o{ COUNT_LINE : "FK"
    PRODUCT ||--o{ COUNT_LINE : "FK"
    COUNT_LINE ||--o{ CONFLICT : "FK"
```

> ℹ️ `DECIMAL_P18S3` correspond à une colonne `DECIMAL(18,3)` dans PostgreSQL. La notation a été ajustée pour rester compatible avec Mermaid.

## Synthèse des contraintes

| Table | Clés principales | Index / Contraintes notables |
| --- | --- | --- |
| `Shop` | `Id` | Index unique `UQ_Shop_LowerName` (nom en minuscule). |
| `ShopUser` | `Id` | Index unique `UQ_ShopUser_Login` (`ShopId`, lower(`Login`)), FK `FK_ShopUser_Shop`. |
| `Product` | `Id` | Index uniques sur `Sku` et `Ean`. |
| `Location` | `Id` | Index unique `IX_Location_Code` (héritage) + `UQ_Location_Shop_Code` (`ShopId`, upper(`Code`)), FK `FK_Location_Shop`. |
| `InventorySession` | `Id` | — |
| `CountingRun` | `Id` | Index partiel `IX_CountingRun_Location_CountType_Open`, index unique `ux_countingrun_active_triplet`, FK optionnelle `FK_CountingRun_Owner`. |
| `CountLine` | `Id` | FK vers `CountingRun` et `Product`. |
| `Conflict` | `Id` | FK vers `CountLine`. |
| `Audit` | `Id` | Index composé `IX_Audit_Entity` (`EntityName`, `EntityId`). |
| `audit_logs` | `id` | Table annexe pour la journalisation technique. |

## Seed disponible

- 5 boutiques (`CinéBoutique Paris`, `Bordeaux`, `Montpellier`, `Marseille`, `Bruxelles`) et rattachement automatique des zones existantes à Paris.
- Zones de démonstration `A` à `E` pour chaque boutique hors Paris.
- Comptes `ShopUser` : `administrateur` (IsAdmin) + `utilisateur1..5` (Paris) ou `utilisateur1..4` (autres boutiques), `Secret_Hash` nul par défaut.
- 39 zones historiques (`B1` à `B20`, `S1` à `S19`) conservées pour compatibilité.
- Aucun produit ni comptage n'est injecté par défaut : toute donnée métier supplémentaire doit être créée via l'API ou des scripts dédiés.

Ces représentations visuelles peuvent être rendues directement dans GitHub grâce au support de Mermaid.
