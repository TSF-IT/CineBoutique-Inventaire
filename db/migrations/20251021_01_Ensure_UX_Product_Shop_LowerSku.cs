namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(2025102101)]
public sealed class EnsureUXProductShopLowerSku : Migration
{
    public override void Up()
    {
        // 1) Crée l’index/contrainte unique (ShopId, lower(Sku)) si absent
        Execute.Sql("""
            DO $$
            BEGIN
                PERFORM 1
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND indexname = 'UX_Product_Shop_LowerSku';
                IF NOT FOUND THEN
                    CREATE UNIQUE INDEX "UX_Product_Shop_LowerSku"
                      ON "Product" ("ShopId", LOWER("Sku"));
                END IF;
            END$$;
        """);
    }

    public override void Down()
    {
        // Optionnel : ne rien faire (ou DROP INDEX si tu veux rollbacker)
        // Execute.Sql(@"DROP INDEX IF EXISTS ""UX_Product_Shop_LowerSku"";");
    }
}
