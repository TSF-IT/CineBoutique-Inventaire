using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202411230001)]
public sealed class AddProductUniqIndexes : Migration
{
    private const string LowerSkuIndexName = "UX_Product_LowerSku";
    private const string EanNotNullIndexName = "UX_Product_Ean_NotNull";

    public override void Up()
    {
        if (Schema.Table("Product").Index("IX_Product_Sku").Exists())
        {
            Delete.Index("IX_Product_Sku").OnTable("Product");
        }

        if (Schema.Table("Product").Index("IX_Product_Ean").Exists())
        {
            Delete.Index("IX_Product_Ean").OnTable("Product");
        }

        Execute.Sql($"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{LowerSkuIndexName}"
            ON "public"."Product" ((LOWER("Sku")));
            """);

        Execute.Sql($"""
            CREATE UNIQUE INDEX IF NOT EXISTS "{EanNotNullIndexName}"
            ON "public"."Product" ("Ean")
            WHERE "Ean" IS NOT NULL;
            """);
    }

    public override void Down()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{LowerSkuIndexName}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{EanNotNullIndexName}\";");

        if (!Schema.Table("Product").Index("IX_Product_Sku").Exists())
        {
            Create.Index("IX_Product_Sku").OnTable("Product").WithOptions().Unique()
                .OnColumn("Sku").Ascending();
        }

        if (!Schema.Table("Product").Index("IX_Product_Ean").Exists())
        {
            Create.Index("IX_Product_Ean").OnTable("Product").WithOptions().Unique()
                .OnColumn("Ean").Ascending();
        }
    }
}
