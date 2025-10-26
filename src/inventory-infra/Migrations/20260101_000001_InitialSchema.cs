using System.Data;
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(20260101000001)]
public sealed class InitialSchema : Migration
{
    private const string ShopTable = "Shop";
    private const string ShopUserTable = "ShopUser";
    private const string ProductGroupTable = "ProductGroup";
    private const string ProductTable = "Product";
    private const string LocationTable = "Location";
    private const string InventorySessionTable = "InventorySession";
    private const string CountingRunTable = "CountingRun";
    private const string CountLineTable = "CountLine";
    private const string ConflictTable = "Conflict";
    private const string AuditTable = "Audit";
    private const string AuditLogsTable = "audit_logs";
    private const string ProductImportTable = "ProductImport";
    private const string ProductImportHistoryTable = "ProductImportHistory";

    public override void Up()
    {
        CreateExtensionsAndHelpers();

        CreateShopTable();
        SeedShops();
        CreateShopUserTable();

        CreateProductGroupTable();
        CreateProductTable();

        CreateLocationTable();
        SeedLocations();

        CreateInventorySessionTable();
        CreateCountingRunTable();
        CreateCountLineTable();
        CreateConflictTable();

        CreateAuditTables();
        CreateProductImportTables();
    }

    public override void Down()
    {
        DropProductImportTables();
        DropAuditTables();

        Delete.Table(ConflictTable).IfExists();
        Delete.Table(CountLineTable).IfExists();
        Delete.Table(CountingRunTable).IfExists();
        Delete.Table(InventorySessionTable).IfExists();
        Delete.Table(LocationTable).IfExists();
        Delete.Table(ProductTable).IfExists();
        Delete.Table(ProductGroupTable).IfExists();
        Delete.Table(ShopUserTable).IfExists();
        Delete.Table(ShopTable).IfExists();

        Execute.Sql(@"DROP FUNCTION IF EXISTS immutable_unaccent(text);");
        Execute.Sql(@"DROP EXTENSION IF EXISTS unaccent;");
        Execute.Sql(@"DROP EXTENSION IF EXISTS pg_trgm;");
        Execute.Sql(@"DROP EXTENSION IF EXISTS pgcrypto;");
        Execute.Sql(@"DROP EXTENSION IF EXISTS ""uuid-ossp"";");
    }

    private void CreateExtensionsAndHelpers()
    {
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS ""uuid-ossp"";");
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS pgcrypto;");
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS unaccent;");
        Execute.Sql(
            """
            CREATE OR REPLACE FUNCTION immutable_unaccent(text)
            RETURNS text
            LANGUAGE sql
            IMMUTABLE
            PARALLEL SAFE
            AS $$ SELECT unaccent('unaccent', $1) $$;
            """);
    }

    private void CreateShopTable()
    {
        Create.Table(ShopTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("Kind").AsCustom("text").NotNullable().WithDefaultValue("boutique");

        Execute.Sql(
            @"ALTER TABLE ""Shop"" ADD CONSTRAINT ck_shop_kind_allowed CHECK (LOWER(""Kind"") IN ('boutique','lumiere','camera'));");

        Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UQ_Shop_LowerName"" ON ""Shop"" (LOWER(""Name""));");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_shop_kind ON ""Shop"" (LOWER(""Kind""));");
    }

    private void SeedShops()
    {
        Execute.Sql(
            """
            INSERT INTO "Shop" ("Id","Name","Kind")
            SELECT uuid_generate_v4(), v.name, v.kind
            FROM (VALUES
                ('CinéBoutique Saint-Denis','boutique'),
                ('CinéBoutique Belgique','boutique'),
                ('Lumière Saint-Denis','lumiere'),
                ('Lumière Marseille','lumiere'),
                ('Lumière Montpellier','lumiere'),
                ('Lumière Bordeaux','lumiere'),
                ('Lumière Belgique','lumiere')
            ) AS v(name, kind)
            WHERE NOT EXISTS (
                SELECT 1 FROM "Shop" s WHERE LOWER(s."Name") = LOWER(v.name)
            );
            """);
    }

    private void CreateShopUserTable()
    {
        Create.Table(ShopUserTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("Login").AsString(128).NotNullable()
            .WithColumn("DisplayName").AsString(256).NotNullable()
            .WithColumn("IsAdmin").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("Secret_Hash").AsString(512).NotNullable().WithDefaultValue(string.Empty)
            .WithColumn("Disabled").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.ForeignKey("FK_ShopUser_Shop")
            .FromTable(ShopUserTable).ForeignColumn("ShopId")
            .ToTable(ShopTable).PrimaryColumn("Id");

        Create.UniqueConstraint("uq_shopuser_shopid_displayname")
            .OnTable(ShopUserTable)
            .Columns("ShopId", "DisplayName");

        Create.Index("ix_shopuser_shopid_displayname")
            .OnTable(ShopUserTable)
            .OnColumn("ShopId").Ascending()
            .OnColumn("DisplayName").Ascending();

        Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UQ_ShopUser_Shop_LowerLogin"" ON ""ShopUser"" (""ShopId"", LOWER(""Login""));");
    }

    private void CreateProductGroupTable()
    {
        Create.Table(ProductGroupTable)
            .WithColumn("Id").AsInt64().PrimaryKey().Identity()
            .WithColumn("Code").AsCustom("text").Nullable()
            .WithColumn("Label").AsCustom("text").NotNullable()
            .WithColumn("ParentId").AsInt64().Nullable();

        Create.ForeignKey($"FK_{ProductGroupTable}_{ProductGroupTable}_ParentId")
            .FromTable(ProductGroupTable).ForeignColumn("ParentId")
            .ToTable(ProductGroupTable).PrimaryColumn("Id")
            .OnDeleteOrUpdate(Rule.SetNull);

        Create.UniqueConstraint("uq_productgroup_code")
            .OnTable(ProductGroupTable)
            .Column("Code");

        Create.Index("ix_productgroup_parent")
            .OnTable(ProductGroupTable)
            .OnColumn("ParentId").Ascending();

        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_productgroup_label_trgm ON ""ProductGroup"" USING GIN (immutable_unaccent(LOWER(""Label"")) gin_trgm_ops);");
    }

    private void CreateProductTable()
    {
        Create.Table(ProductTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("Sku").AsString(32).NotNullable()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("Ean").AsString(64).Nullable()
            .WithColumn("CodeDigits").AsString(64).Nullable()
            .WithColumn("Attributes").AsCustom("jsonb").NotNullable()
            .WithColumn("GroupId").AsInt64().Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable();

        Execute.Sql(@"ALTER TABLE ""Product"" ALTER COLUMN ""Attributes"" SET DEFAULT '{}'::jsonb;");

        Create.ForeignKey("FK_Product_Shop_ShopId")
            .FromTable(ProductTable).ForeignColumn("ShopId")
            .ToTable(ShopTable).PrimaryColumn("Id")
            .OnDeleteOrUpdate(Rule.Cascade);

        Create.ForeignKey("FK_Product_ProductGroup_GroupId")
            .FromTable(ProductTable).ForeignColumn("GroupId")
            .ToTable(ProductGroupTable).PrimaryColumn("Id")
            .OnDeleteOrUpdate(Rule.SetNull);

        Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Product_Shop_LowerSku"" ON ""Product"" (""ShopId"", LOWER(""Sku""));");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Product_Shop_LowerSku"" ON ""Product"" (""ShopId"", LOWER(""Sku""));");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Product_Shop_Ean"" ON ""Product"" (""ShopId"", ""Ean"") WHERE ""Ean"" IS NOT NULL;");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Product_Shop_LowerEan"" ON ""Product"" (""ShopId"", LOWER(""Ean""));");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Product_Shop_CodeDigits"" ON ""Product"" (""ShopId"", ""CodeDigits"");");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_codedigits ON ""Product"" (""CodeDigits"");");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_attributes_gin ON ""Product"" USING GIN (""Attributes"");");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_sku_trgm ON ""Product"" USING GIN (LOWER(""Sku"") gin_trgm_ops);");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_ean_trgm ON ""Product"" USING GIN (LOWER(""Ean"") gin_trgm_ops);");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_name_trgm ON ""Product"" USING GIN (immutable_unaccent(LOWER(""Name"")) gin_trgm_ops);");
        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Product_Shop_Name_trgm"" ON ""Product"" USING GIN (""ShopId"", immutable_unaccent(LOWER(""Name"")) gin_trgm_ops);");
    }

    private void CreateLocationTable()
    {
        Create.Table(LocationTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("Code").AsString(32).NotNullable()
            .WithColumn("Label").AsString(128).NotNullable()
            .WithColumn("Disabled").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.ForeignKey("FK_Location_Shop")
            .FromTable(LocationTable).ForeignColumn("ShopId")
            .ToTable(ShopTable).PrimaryColumn("Id");

        Execute.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UQ_Location_Shop_Code"" ON ""Location"" (""ShopId"", UPPER(""Code""));");

        Create.Index("IX_Location_ShopId_Code")
            .OnTable(LocationTable)
            .OnColumn("ShopId").Ascending()
            .OnColumn("Code").Ascending();
    }

    private void SeedLocations()
    {
        Execute.Sql(
            """
            DO $$
            DECLARE
                target_shop_id uuid;
                zone_code text;
            BEGIN
                SELECT "Id" INTO target_shop_id
                FROM "Shop"
                WHERE LOWER("Name") = LOWER('CinéBoutique Saint-Denis')
                ORDER BY "Id"
                LIMIT 1;

                IF target_shop_id IS NULL THEN
                    RETURN;
                END IF;

                FOR zone_code IN SELECT 'B' || i::text FROM generate_series(1, 20) AS s(i) LOOP
                    INSERT INTO "Location" ("Id","ShopId","Code","Label","Disabled")
                    SELECT uuid_generate_v4(), target_shop_id, zone_code, 'Zone ' || zone_code, FALSE
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "Location"
                        WHERE "ShopId" = target_shop_id AND "Code" = zone_code
                    );
                END LOOP;

                FOR zone_code IN SELECT 'S' || i::text FROM generate_series(1, 19) AS s(i) LOOP
                    INSERT INTO "Location" ("Id","ShopId","Code","Label","Disabled")
                    SELECT uuid_generate_v4(), target_shop_id, zone_code, 'Zone ' || zone_code, FALSE
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "Location"
                        WHERE "ShopId" = target_shop_id AND "Code" = zone_code
                    );
                END LOOP;
            END $$;
            """);
    }

    private void CreateInventorySessionTable()
    {
        Create.Table(InventorySessionTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("StartedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("CompletedAtUtc").AsDateTimeOffset().Nullable();
    }

    private void CreateCountingRunTable()
    {
        Create.Table(CountingRunTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("InventorySessionId").AsGuid().NotNullable()
            .WithColumn("LocationId").AsGuid().NotNullable()
            .WithColumn("CountType").AsInt16().NotNullable().WithDefaultValue(1)
            .WithColumn("StartedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("CompletedAtUtc").AsDateTimeOffset().Nullable()
            .WithColumn("OperatorDisplayName").AsString(200).NotNullable().WithDefaultValue("Unknown")
            .WithColumn("OwnerUserId").AsGuid().Nullable();

        Create.ForeignKey("FK_CountingRun_InventorySession")
            .FromTable(CountingRunTable).ForeignColumn("InventorySessionId")
            .ToTable(InventorySessionTable).PrimaryColumn("Id");

        Create.ForeignKey("FK_CountingRun_Location")
            .FromTable(CountingRunTable).ForeignColumn("LocationId")
            .ToTable(LocationTable).PrimaryColumn("Id");

        Create.ForeignKey("FK_CountingRun_OwnerUser")
            .FromTable(CountingRunTable).ForeignColumn("OwnerUserId")
            .ToTable(ShopUserTable).PrimaryColumn("Id");

        Create.Index("ix_countingrun_owneruserid")
            .OnTable(CountingRunTable)
            .OnColumn("OwnerUserId").Ascending();

        Execute.Sql(
            """
            CREATE INDEX IF NOT EXISTS "IX_CountingRun_Location_CountType_Open"
            ON "CountingRun" ("LocationId", "CountType")
            WHERE "CompletedAtUtc" IS NULL;
            """);

        Execute.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_countingrun_active_triplet
            ON "CountingRun" ("InventorySessionId", "LocationId", "CountType", "OperatorDisplayName")
            WHERE "CompletedAtUtc" IS NULL;
            """);
    }

    private void CreateCountLineTable()
    {
        Create.Table(CountLineTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("CountingRunId").AsGuid().NotNullable()
            .WithColumn("ProductId").AsGuid().NotNullable()
            .WithColumn("Quantity").AsDecimal(18, 3).NotNullable()
            .WithColumn("CountedAtUtc").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("FK_CountLine_CountingRun")
            .FromTable(CountLineTable).ForeignColumn("CountingRunId")
            .ToTable(CountingRunTable).PrimaryColumn("Id");

        Create.ForeignKey("FK_CountLine_Product")
            .FromTable(CountLineTable).ForeignColumn("ProductId")
            .ToTable(ProductTable).PrimaryColumn("Id");
    }

    private void CreateConflictTable()
    {
        Create.Table(ConflictTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("CountLineId").AsGuid().NotNullable()
            .WithColumn("Status").AsString(64).NotNullable()
            .WithColumn("Notes").AsString(1024).Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable()
            .WithColumn("ResolvedAtUtc").AsDateTimeOffset().Nullable();

        Create.ForeignKey("FK_Conflict_CountLine")
            .FromTable(ConflictTable).ForeignColumn("CountLineId")
            .ToTable(CountLineTable).PrimaryColumn("Id");
    }

    private void CreateAuditTables()
    {
        Create.Table(AuditTable)
            .WithColumn("Id").AsGuid().PrimaryKey().WithDefaultValue(SystemMethods.NewGuid)
            .WithColumn("EntityName").AsString(256).NotNullable()
            .WithColumn("EntityId").AsString(128).NotNullable()
            .WithColumn("EventType").AsString(64).NotNullable()
            .WithColumn("Payload").AsCustom("jsonb").Nullable()
            .WithColumn("CreatedAtUtc").AsDateTimeOffset().NotNullable();

        Create.Index("IX_Audit_Entity")
            .OnTable(AuditTable)
            .OnColumn("EntityName").Ascending()
            .OnColumn("EntityId").Ascending();

        Create.Table(AuditLogsTable)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("at").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime)
            .WithColumn("actor").AsString(320).Nullable()
            .WithColumn("message").AsCustom("text").NotNullable()
            .WithColumn("category").AsString(200).Nullable();
    }

    private void DropAuditTables()
    {
        Delete.Table(AuditLogsTable).IfExists();
        Delete.Table(AuditTable).IfExists();
    }

    private void CreateProductImportTables()
    {
        Create.Table(ProductImportTable)
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("FileName").AsCustom("text").NotNullable()
            .WithColumn("FileHashSha256").AsCustom("char(64)").NotNullable()
            .WithColumn("RowCount").AsInt32().NotNullable()
            .WithColumn("ImportedAtUtc").AsDateTimeOffset().NotNullable().WithDefaultValue(SystemMethods.CurrentUTCDateTime);

        Create.ForeignKey("fk_productimport_shop_shopid")
            .FromTable(ProductImportTable).ForeignColumn("ShopId")
            .ToTable(ShopTable).PrimaryColumn("Id")
            .OnDeleteOrUpdate(Rule.Cascade);

        Create.UniqueConstraint("uq_productimport_shopid")
            .OnTable(ProductImportTable)
            .Column("ShopId");

        Create.Index("ux_productimport_shopid_filehash")
            .OnTable(ProductImportTable)
            .OnColumn("ShopId").Ascending()
            .OnColumn("FileHashSha256").Ascending()
            .WithOptions().Unique();

        Create.Table(ProductImportHistoryTable)
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("ShopId").AsGuid().NotNullable()
            .WithColumn("StartedAt").AsDateTimeOffset().NotNullable()
            .WithColumn("FinishedAt").AsDateTimeOffset().Nullable()
            .WithColumn("Username").AsString().Nullable()
            .WithColumn("FileSha256").AsString(128).Nullable()
            .WithColumn("TotalLines").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Inserted").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ErrorCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("Status").AsString(32).NotNullable()
            .WithColumn("DurationMs").AsInt32().Nullable();

        Create.ForeignKey("FK_ProductImportHistory_Shop")
            .FromTable(ProductImportHistoryTable).ForeignColumn("ShopId")
            .ToTable(ShopTable).PrimaryColumn("Id");

        Create.Index("IX_ProductImportHistory_StartedAt")
            .OnTable(ProductImportHistoryTable)
            .OnColumn("StartedAt").Descending();

        Create.Index("IX_ProductImportHistory_FileSha256")
            .OnTable(ProductImportHistoryTable)
            .OnColumn("FileSha256").Ascending();

        Create.Index("IX_ProductImportHistory_ShopId")
            .OnTable(ProductImportHistoryTable)
            .OnColumn("ShopId").Ascending();
    }

    private void DropProductImportTables()
    {
        Delete.Table(ProductImportHistoryTable).IfExists();
        Delete.Table(ProductImportTable).IfExists();
    }
}
