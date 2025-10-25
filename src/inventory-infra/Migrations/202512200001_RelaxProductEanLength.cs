using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202512200001)]
public sealed class RelaxProductEanLength : Migration
{
    private const string TableName = "Product";
    private const string ColumnName = "Ean";

    public override void Up()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Execute.Sql($"""
ALTER TABLE "{TableName}" ALTER COLUMN "{ColumnName}" TYPE VARCHAR(64);
""");
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Execute.Sql($"""
ALTER TABLE "{TableName}" ALTER COLUMN "{ColumnName}" TYPE VARCHAR(13) USING SUBSTRING("{ColumnName}", 1, 13);
""");
        }
    }
}
