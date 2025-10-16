using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202501150001)]
public sealed class RelaxProductCodeAndAddCodeDigits : Migration
{
    private const string TableName = "Product";
    private const string CodeDigitsColumn = "CodeDigits";
    private const string CodeDigitsIndexName = "IX_Product_CodeDigits";
    private const string FilteredEanIndexName = "UX_Product_Ean_NotNull";
    private const string LegacyEanIndexName = "IX_Product_Ean";

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(CodeDigitsColumn).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(CodeDigitsColumn)
                .AsString(64)
                .Nullable();
        }

        if (Schema.Table(TableName).Index(LegacyEanIndexName).Exists())
        {
            Delete.Index(LegacyEanIndexName).OnTable(TableName);
        }

        Execute.Sql($"DROP INDEX IF EXISTS \"{FilteredEanIndexName}\";");

        if (!Schema.Table(TableName).Index(CodeDigitsIndexName).Exists())
        {
            Create.Index(CodeDigitsIndexName)
                .OnTable(TableName)
                .OnColumn(CodeDigitsColumn).Ascending();
        }

        Execute.Sql(
            $"UPDATE \"{TableName}\" SET \"{CodeDigitsColumn}\" = regexp_replace(COALESCE(\"Ean\", ''), '[^0-9]', '', 'g') " +
            "WHERE \"Ean\" IS NOT NULL AND (\"CodeDigits\" IS NULL OR \"CodeDigits\" <> regexp_replace(COALESCE(\"Ean\", ''), '[^0-9]', '', 'g'));"
        );
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Index(CodeDigitsIndexName).Exists())
        {
            Delete.Index(CodeDigitsIndexName).OnTable(TableName);
        }

        if (Schema.Table(TableName).Column(CodeDigitsColumn).Exists())
        {
            Delete.Column(CodeDigitsColumn).FromTable(TableName);
        }

        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{FilteredEanIndexName}\" ON \"public\".\"{TableName}\" (\"Ean\") WHERE \"Ean\" IS NOT NULL;");
    }
}
