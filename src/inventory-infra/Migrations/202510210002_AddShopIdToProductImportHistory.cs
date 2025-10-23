using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202510210002)]
public sealed class AddShopIdToProductImportHistory : Migration
{
    private const string TableName = "ProductImportHistory";
    private const string ShopIdColumn = "ShopId";
    private const string StartedAtColumn = "StartedAt";
    private const string IndexName = "IX_ProductImportHistory_ShopId_StartedAt";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            return;
        }

        if (!Schema.Table(TableName).Column(ShopIdColumn).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(ShopIdColumn)
                .AsGuid()
                .Nullable();
        }

        if (!Schema.Table(TableName).Index(IndexName).Exists())
        {
            Create.Index(IndexName)
                .OnTable(TableName)
                .OnColumn(ShopIdColumn).Ascending()
                .OnColumn(StartedAtColumn).Descending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Index(IndexName).Exists())
        {
            Delete.Index(IndexName).OnTable(TableName);
        }

        if (Schema.Table(TableName).Column(ShopIdColumn).Exists())
        {
            Delete.Column(ShopIdColumn).FromTable(TableName);
        }
    }
}
