using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202512150001)]
public sealed class RemoveProductDescription : Migration
{
    private const string ProductTable = "Product";
    private const string DescriptionColumn = "Description";
    private const string DescriptionIndexName = "IX_Product_Shop_Descr_trgm";

    public override void Up()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{DescriptionIndexName}\";");

        if (Schema.Table(ProductTable).Column(DescriptionColumn).Exists())
        {
            Delete.Column(DescriptionColumn).FromTable(ProductTable);
        }
    }

    public override void Down()
    {
        if (!Schema.Table(ProductTable).Column(DescriptionColumn).Exists())
        {
            Alter.Table(ProductTable)
                .AddColumn(DescriptionColumn)
                .AsCustom("TEXT")
                .Nullable();
        }

        Execute.Sql($"""
CREATE INDEX IF NOT EXISTS {DescriptionIndexName}
    ON "Product" USING GIN ("ShopId", immutable_unaccent(LOWER("{DescriptionColumn}")) gin_trgm_ops);
"""
        );
    }
}
