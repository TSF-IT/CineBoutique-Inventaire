# Diagramme BDD (Mermaid)

Diagramme généré en Mermaid à partir des migrations actuelles. La source est dans `docs/BDD/diagramme.mmd` et peut être copiée dans un éditeur Mermaid compatible pour rendu.

```mermaid
erDiagram
    SHOP ||--o{ LOCATION : "contient"
    SHOP ||--o{ SHOP_USER : "emploie"
    SHOP ||--o{ PRODUCT : "catalogue"
    SHOP ||--|| PRODUCT_IMPORT : "import en cours"
    SHOP ||--o{ PRODUCT_IMPORT_HISTORY : "archives"
    PRODUCT_GROUP ||--o{ PRODUCT_GROUP : "parent"
    PRODUCT_GROUP ||--o{ PRODUCT : "classifie"
    INVENTORY_SESSION ||--o{ COUNTING_RUN : "planifie"
    LOCATION ||--o{ COUNTING_RUN : "associe"
    SHOP_USER ||--o{ COUNTING_RUN : "proprietaire"
    COUNTING_RUN ||--o{ COUNT_LINE : "compose"
    PRODUCT ||--o{ COUNT_LINE : "concerne"
    COUNT_LINE ||--o{ CONFLICT : "peut generer"
```
