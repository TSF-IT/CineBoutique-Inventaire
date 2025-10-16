-- Ajout de la colonne CodeDigits sur Product et indexation.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'Product'
          AND column_name = 'CodeDigits'
    ) THEN
        ALTER TABLE "Product" ADD COLUMN "CodeDigits" text NULL;
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS "IX_Product_CodeDigits" ON "Product" ("CodeDigits");

UPDATE "Product"
SET "CodeDigits" = REGEXP_REPLACE(COALESCE("Ean", ''), '[^0-9]', '', 'g');
