using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202512010002)]
public sealed class AllowDuplicateProductEans : Migration
{
    private const string ProductTable = "Product";
    private const string ProductShopIdColumn = "ShopId";
    private const string ProductEanColumn = "Ean";

    private const string UniqueIndexName = "UX_Product_Shop_Ean_NotNull";
    private const string NonUniqueIndexName = "IX_Product_Shop_Ean";

    public override void Up()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{UniqueIndexName}\";");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS \"{NonUniqueIndexName}\" ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductEanColumn}\") WHERE \"{ProductEanColumn}\" IS NOT NULL;");
    }

    public override void Down()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{NonUniqueIndexName}\";");
        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{UniqueIndexName}\" ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductEanColumn}\") WHERE \"{ProductEanColumn}\" IS NOT NULL;");
    }
}
