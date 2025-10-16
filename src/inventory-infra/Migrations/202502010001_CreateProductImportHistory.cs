using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202502010001)]
public sealed class CreateProductImportHistory : Migration
{
    private const string TableName = "ProductImportHistory";
    private const string StartedAtIndexName = "IX_ProductImportHistory_StartedAt";
    private const string FileShaIndexName = "IX_ProductImportHistory_FileSha256";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            Create.Table(TableName)
                .WithColumn("Id").AsGuid().PrimaryKey()
                .WithColumn("StartedAt").AsDateTimeOffset().NotNullable()
                .WithColumn("FinishedAt").AsDateTimeOffset().Nullable()
                .WithColumn("Username").AsString().Nullable()
                .WithColumn("FileSha256").AsString(128).Nullable()
                .WithColumn("TotalLines").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Inserted").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("ErrorCount").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn("Status").AsString(32).NotNullable()
                .WithColumn("DurationMs").AsInt32().Nullable();
        }

        if (!Schema.Table(TableName).Index(StartedAtIndexName).Exists())
        {
            Create.Index(StartedAtIndexName)
                .OnTable(TableName)
                .OnColumn("StartedAt").Descending();
        }

        if (!Schema.Table(TableName).Index(FileShaIndexName).Exists())
        {
            Create.Index(FileShaIndexName)
                .OnTable(TableName)
                .OnColumn("FileSha256").Ascending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Index(FileShaIndexName).Exists())
        {
            Delete.Index(FileShaIndexName).OnTable(TableName);
        }

        if (Schema.Table(TableName).Index(StartedAtIndexName).Exists())
        {
            Delete.Index(StartedAtIndexName).OnTable(TableName);
        }

        if (Schema.Table(TableName).Exists())
        {
            Delete.Table(TableName);
        }
    }
}
