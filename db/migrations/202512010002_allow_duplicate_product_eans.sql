-- Migration: allow duplicate product EANs per shop
DROP INDEX IF EXISTS "UX_Product_Shop_Ean_NotNull";

CREATE INDEX IF NOT EXISTS "IX_Product_Shop_Ean"
    ON "Product" ("ShopId", "Ean")
    WHERE "Ean" IS NOT NULL;
