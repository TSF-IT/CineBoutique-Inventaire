using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202601010001)]
public sealed class ExtendProductEanLength : Migration
{
    private const string TableName = "Product";
    private const string ColumnName = "Ean";

    public override void Up()
    {
        Alter.Table(TableName)
            .AlterColumn(ColumnName).AsString(64).Nullable();
    }

    public override void Down()
    {
        Alter.Table(TableName)
            .AlterColumn(ColumnName).AsString(13).Nullable();
    }
}
