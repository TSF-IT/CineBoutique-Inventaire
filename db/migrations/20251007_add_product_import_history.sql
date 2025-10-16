-- Création de la table ProductImportHistory pour tracer les importations produits.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_name = 'ProductImportHistory'
          AND table_schema = 'public'
    ) THEN
        CREATE TABLE "ProductImportHistory" (
            "Id" uuid PRIMARY KEY,
            "StartedAt" timestamptz NOT NULL,
            "FinishedAt" timestamptz NULL,
            "Username" text NULL,
            "FileSha256" text NULL,
            "TotalLines" int NOT NULL,
            "Inserted" int NOT NULL,
            "ErrorCount" int NOT NULL,
            "Status" text NOT NULL,
            "DurationMs" int NULL
        );
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS "IX_ProductImportHistory_StartedAt" ON "ProductImportHistory" ("StartedAt" DESC);
CREATE INDEX IF NOT EXISTS "IX_ProductImportHistory_FileSha256" ON "ProductImportHistory" ("FileSha256");
