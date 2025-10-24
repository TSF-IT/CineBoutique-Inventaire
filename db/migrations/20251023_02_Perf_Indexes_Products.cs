using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(2025102302)]
public sealed class Perf_Indexes_Products : Migration
{
    public override void Up()
    {
        // Unicité déjà gérée : UX_Product_Shop_LowerSku

        // Préfixe/ILIKE sur SKU/EAN/CodeDigits/Name scope boutique
        Execute.Sql(
            """
            CREATE INDEX IF NOT EXISTS "IX_Product_Shop_LowerSku"
                ON "Product" ("ShopId", LOWER("Sku"));
            CREATE INDEX IF NOT EXISTS "IX_Product_Shop_LowerEan"
                ON "Product" ("ShopId", LOWER("Ean"));
            CREATE INDEX IF NOT EXISTS "IX_Product_Shop_CodeDigits"
                ON "Product" ("ShopId", "CodeDigits");
            CREATE INDEX IF NOT EXISTS "IX_Product_Shop_Name_trgm"
                ON "Product" USING GIN ("ShopId", immutable_unaccent(LOWER("Name")) gin_trgm_ops);
            """
        );
    }

    public override void Down()
    {
        // No-op : conserver les index
    }
}
