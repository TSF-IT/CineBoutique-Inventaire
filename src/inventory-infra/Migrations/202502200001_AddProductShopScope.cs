using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202502200001)]
public sealed class AddProductShopScope : Migration
{
    private const string ProductTable = "Product";
    private const string ProductHistoryTable = "ProductImportHistory";
    private const string ShopIdColumn = "ShopId";
    private const string LowerSkuIndexName = "UX_Product_Shop_LowerSku";
    private const string EanIndexName = "UX_Product_Shop_Ean_NotNull";
    private const string HistoryShopStartedIndexName = "IX_ProductImportHistory_Shop_StartedAt";
    private const string HistoryFileIndexName = "IX_ProductImportHistory_FileSha256";

    public override void Up()
    {
        const string uuidNil = "00000000-0000-0000-0000-000000000000";

        if (!Schema.Table(ProductTable).Column(ShopIdColumn).Exists())
        {
            Alter.Table(ProductTable)
                .AddColumn(ShopIdColumn)
                .AsGuid()
                .NotNullable()
                .WithDefaultValue(uuidNil);
        }

        Execute.Sql($"UPDATE \"{ProductTable}\" SET \"{ShopIdColumn}\" = '{uuidNil}' WHERE \"{ShopIdColumn}\" IS NULL;");
        Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{ShopIdColumn}\" DROP DEFAULT;");

        Execute.Sql($"DROP INDEX IF EXISTS \"{LowerSkuIndexName}\";");
        Execute.Sql("DROP INDEX IF EXISTS \"UX_Product_LowerSku\";");
        Execute.Sql("DROP INDEX IF EXISTS \"IX_Product_Sku\";");

        Execute.Sql($"DROP INDEX IF EXISTS \"{EanIndexName}\";");
        Execute.Sql("DROP INDEX IF EXISTS \"UX_Product_Ean_NotNull\";");
        Execute.Sql("DROP INDEX IF EXISTS \"IX_Product_Ean\";");

        Execute.Sql($"""
CREATE UNIQUE INDEX IF NOT EXISTS "{LowerSkuIndexName}"
ON "public"."{ProductTable}" ("{ShopIdColumn}", (LOWER("Sku")));
""");

        Execute.Sql($"""
CREATE UNIQUE INDEX IF NOT EXISTS "{EanIndexName}"
ON "public"."{ProductTable}" ("{ShopIdColumn}", "Ean")
WHERE "Ean" IS NOT NULL;
""");

        if (!Schema.Table(ProductHistoryTable).Column(ShopIdColumn).Exists())
        {
            Alter.Table(ProductHistoryTable)
                .AddColumn(ShopIdColumn)
                .AsGuid()
                .NotNullable()
                .WithDefaultValue(uuidNil);
        }

        Execute.Sql($"UPDATE \"{ProductHistoryTable}\" SET \"{ShopIdColumn}\" = '{uuidNil}' WHERE \"{ShopIdColumn}\" IS NULL;");
        Execute.Sql($"ALTER TABLE \"{ProductHistoryTable}\" ALTER COLUMN \"{ShopIdColumn}\" DROP DEFAULT;");

        if (!Schema.Table(ProductHistoryTable).Index(HistoryShopStartedIndexName).Exists())
        {
            Create.Index(HistoryShopStartedIndexName)
                .OnTable(ProductHistoryTable)
                .OnColumn(ShopIdColumn).Ascending()
                .OnColumn("StartedAt").Descending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(ProductHistoryTable).Index(HistoryShopStartedIndexName).Exists())
        {
            Delete.Index(HistoryShopStartedIndexName).OnTable(ProductHistoryTable);
        }

        if (Schema.Table(ProductHistoryTable).Column(ShopIdColumn).Exists())
        {
            Delete.Column(ShopIdColumn).FromTable(ProductHistoryTable);
        }

        if (Schema.Table(ProductTable).Index(LowerSkuIndexName).Exists())
        {
            Delete.Index(LowerSkuIndexName).OnTable(ProductTable);
        }

        if (Schema.Table(ProductTable).Index(EanIndexName).Exists())
        {
            Delete.Index(EanIndexName).OnTable(ProductTable);
        }

        if (Schema.Table(ProductTable).Column(ShopIdColumn).Exists())
        {
            Delete.Column(ShopIdColumn).FromTable(ProductTable);
        }

        Execute.Sql("""
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Product_LowerSku"
ON "public"."Product" ((LOWER("Sku")));
""");

        Execute.Sql("""
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Product_Ean_NotNull"
ON "public"."Product" ("Ean") WHERE "Ean" IS NOT NULL;
""");

        if (!Schema.Table(ProductHistoryTable).Index(HistoryFileIndexName).Exists())
        {
            Create.Index(HistoryFileIndexName)
                .OnTable(ProductHistoryTable)
                .OnColumn("FileSha256").Ascending();
        }
    }
}
