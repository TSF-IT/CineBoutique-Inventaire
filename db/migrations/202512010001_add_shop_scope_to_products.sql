-- Migration: Add shop scope to products and import history
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM "Product") THEN
        RAISE EXCEPTION 'AddShopScopeToProducts requires an empty "Product" table.';
    END IF;
END$$;

ALTER TABLE "Product"
    ADD COLUMN IF NOT EXISTS "ShopId" uuid;

ALTER TABLE "Product"
    ALTER COLUMN "ShopId" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE constraint_name = 'FK_Product_Shop_ShopId'
          AND table_schema = current_schema()
    ) THEN
        ALTER TABLE "Product"
            ADD CONSTRAINT "FK_Product_Shop_ShopId"
            FOREIGN KEY ("ShopId") REFERENCES "Shop"("Id")
            ON UPDATE CASCADE ON DELETE CASCADE;
    END IF;
END$$;

DROP INDEX IF EXISTS "UX_Product_LowerSku";
DROP INDEX IF EXISTS "UX_Product_Ean_NotNull";
DROP INDEX IF EXISTS "IX_Product_CodeDigits";

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Product_Shop_LowerSku"
    ON "Product" ("ShopId", LOWER("Sku"));
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Product_Shop_Ean_NotNull"
    ON "Product" ("ShopId", "Ean")
    WHERE "Ean" IS NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_Product_Shop_CodeDigits"
    ON "Product" ("ShopId", "CodeDigits");

DO $$
BEGIN
    IF to_regclass('"ProductImportHistory"') IS NULL THEN
        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = current_schema()
          AND table_name = 'ProductImportHistory'
          AND column_name = 'ShopId'
    ) THEN
        ALTER TABLE "ProductImportHistory"
            ADD COLUMN "ShopId" uuid;

        UPDATE "ProductImportHistory"
        SET "ShopId" = '00000000-0000-0000-0000-000000000000'
        WHERE "ShopId" IS NULL;

        ALTER TABLE "ProductImportHistory"
            ALTER COLUMN "ShopId" SET NOT NULL;
    END IF;

    PERFORM 1
    FROM pg_indexes
    WHERE schemaname = current_schema()
      AND indexname = 'IX_ProductImportHistory_ShopId';

    IF NOT FOUND THEN
        CREATE INDEX "IX_ProductImportHistory_ShopId"
            ON "ProductImportHistory" ("ShopId");
    END IF;
END$$;
