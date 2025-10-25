using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202512250001)]
public sealed class AddLocationDisabledFlag : Migration
{
    private const string TableName = "Location";
    private const string ColumnName = "Disabled";

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(ColumnName)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        Execute.Sql($@"UPDATE ""{TableName}"" SET ""{ColumnName}"" = FALSE WHERE ""{ColumnName}"" IS NULL;");
        Execute.Sql($@"ALTER TABLE ""{TableName}"" ALTER COLUMN ""{ColumnName}"" SET DEFAULT FALSE;");
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Delete.Column(ColumnName).FromTable(TableName);
        }
    }
}
