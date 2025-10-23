using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202510210003)]
public sealed class AddShopScopedProductSkuIndex : Migration
{
    private const string TableName = "Product";
    private const string ShopIdColumn = "ShopId";
    private const string SkuColumn = "Sku";
    private const string LegacyIndexName = "ux_product_shopid_sku";
    private const string LowerSkuIndexName = "ux_product_shopid_lowersku";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            return;
        }

        Execute.Sql($"DROP INDEX IF EXISTS {LegacyIndexName};");
        Execute.Sql($"""
CREATE UNIQUE INDEX IF NOT EXISTS {LowerSkuIndexName}
ON \"{TableName}\" (\"{ShopIdColumn}\", LOWER(\"{SkuColumn}\"))
WHERE \"{SkuColumn}\" IS NOT NULL;
""");
    }

    public override void Down()
    {
        Execute.Sql($"DROP INDEX IF EXISTS {LowerSkuIndexName};");
        Execute.Sql($"""
CREATE UNIQUE INDEX IF NOT EXISTS {LegacyIndexName}
ON \"{TableName}\" (\"{ShopIdColumn}\", \"{SkuColumn}\")
WHERE \"{SkuColumn}\" IS NOT NULL;
""");
    }
}
