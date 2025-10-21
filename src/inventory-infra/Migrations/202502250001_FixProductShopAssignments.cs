using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202502250001)]
public sealed class FixProductShopAssignments : Migration
{
    private const string ProductTable = "Product";
    private const string ProductHistoryTable = "ProductImportHistory";
    private const string ShopTable = "Shop";
    private const string ShopIdColumn = "ShopId";
    private const string ShopSkuIndex = "ix_product_shop_sku";
    private const string ShopEanIndex = "ix_product_shop_ean";
    private const string ShopDigitsIndex = "ix_product_shop_digits";
    private const string LegacyUniqueEanIndex = "UX_Product_Shop_Ean_NotNull";
    private const string LegacyUniqueEanIndex2 = "UX_Product_Ean_NotNull";
    private const string LegacyEanIndex = "IX_Product_Ean";
    private const string LegacyCodeDigitsIndex = "IX_Product_CodeDigits";

    public override void Up()
    {
        Execute.Sql(
            """
            INSERT INTO "Shop"("Id", "Name")
            SELECT uuid_generate_v4(), 'Legacy'
            WHERE NOT EXISTS (SELECT 1 FROM "Shop");
            """);

        Execute.Sql(
            $"""
            WITH selected_shop AS (
                SELECT "Id" FROM "{ShopTable}" ORDER BY "Name" LIMIT 1
            )
            UPDATE "{ProductTable}" p
            SET "{ShopIdColumn}" = (SELECT "Id" FROM selected_shop)
            WHERE p."{ShopIdColumn}" IS NULL OR p."{ShopIdColumn}" = '00000000-0000-0000-0000-000000000000';
            """);

        Execute.Sql(
            $"""
            WITH selected_shop AS (
                SELECT "Id" FROM "{ShopTable}" ORDER BY "Name" LIMIT 1
            )
            UPDATE "{ProductHistoryTable}" h
            SET "{ShopIdColumn}" = (SELECT "Id" FROM selected_shop)
            WHERE h."{ShopIdColumn}" IS NULL OR h."{ShopIdColumn}" = '00000000-0000-0000-0000-000000000000';
            """);

        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyUniqueEanIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyUniqueEanIndex2}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyEanIndex}\";");

        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyCodeDigitsIndex}\";");

        Execute.Sql(
            $"""
            CREATE INDEX IF NOT EXISTS "{ShopSkuIndex}" ON "{ProductTable}"("{ShopIdColumn}", "Sku");
            """);

        Execute.Sql(
            $"""
            CREATE INDEX IF NOT EXISTS "{ShopEanIndex}" ON "{ProductTable}"("{ShopIdColumn}", "Ean");
            """);

        Execute.Sql(
            $"""
            CREATE INDEX IF NOT EXISTS "{ShopDigitsIndex}" ON "{ProductTable}"("{ShopIdColumn}", "CodeDigits");
            """);
    }

    public override void Down()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{ShopSkuIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{ShopEanIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{ShopDigitsIndex}\";");

        Execute.Sql(
            $"""
            CREATE INDEX IF NOT EXISTS "{LegacyCodeDigitsIndex}" ON "{ProductTable}" ("CodeDigits");
            """);

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{LegacyUniqueEanIndex}" ON "{ProductTable}" ("{ShopIdColumn}", "Ean") WHERE "Ean" IS NOT NULL;
            """);

        Execute.Sql(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{LegacyUniqueEanIndex2}" ON "{ProductTable}" ("Ean") WHERE "Ean" IS NOT NULL;
            """);

        Execute.Sql(
            $"""
            CREATE INDEX IF NOT EXISTS "{LegacyEanIndex}" ON "{ProductTable}" ("Ean");
            """);
    }
}
