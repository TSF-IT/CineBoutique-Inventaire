using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202411150001)]
public sealed class AddLocationShopIndex : Migration
{
    private const string LocationsTable = "Location";
    private const string IndexName = "IX_Location_ShopId_Code";

    public override void Up()
    {
        Execute.Sql($"""
CREATE INDEX IF NOT EXISTS "{IndexName}" ON "{LocationsTable}" ("ShopId", "Code");
""");
    }

    public override void Down()
    {
        Execute.Sql($"""
DROP INDEX IF EXISTS "{IndexName}";
""");
    }
}
