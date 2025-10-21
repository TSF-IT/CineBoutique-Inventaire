using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202502270001)]
public sealed class EnsureProductImportDedup : Migration
{
    private const string TableName = "ProductImport";
    private const string IdColumn = "Id";
    private const string ShopIdColumn = "ShopId";
    private const string FileNameColumn = "FileName";
    private const string FileHashColumn = "FileHashSha256";
    private const string RowCountColumn = "RowCount";
    private const string ImportedAtColumn = "ImportedAtUtc";
    private const string ShopForeignKeyName = "fk_productimport_shop_shopid";
    private const string ShopHashUniqueIndexName = "ux_productimport_shopid_filehash";
    private const string LegacyUniqueConstraintName = "uq_productimport_shopid";
    private const string LegacyUniqueIndexName = "ux_productimport_shopid";

    public override void Up()
    {
        if (!Schema.Table(TableName).Exists())
        {
            Create.Table(TableName)
                .WithColumn(IdColumn).AsGuid().PrimaryKey()
                .WithColumn(ShopIdColumn).AsGuid().NotNullable()
                .WithColumn(FileNameColumn).AsCustom("TEXT").NotNullable()
                .WithColumn(FileHashColumn).AsString(64).NotNullable()
                .WithColumn(RowCountColumn).AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn(ImportedAtColumn).AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

            Create.ForeignKey(ShopForeignKeyName)
                .FromTable(TableName).ForeignColumn(ShopIdColumn)
                .ToTable("Shop").PrimaryColumn("Id");

            Execute.Sql($"ALTER TABLE \"{TableName}\" ALTER COLUMN \"{RowCountColumn}\" DROP DEFAULT;");
            Execute.Sql($"ALTER TABLE \"{TableName}\" ALTER COLUMN \"{ImportedAtColumn}\" DROP DEFAULT;");
        }
        else
        {
            if (!Schema.Table(TableName).Column(RowCountColumn).Exists())
            {
                Alter.Table(TableName)
                    .AddColumn(RowCountColumn).AsInt32().NotNullable().WithDefaultValue(0);
                Execute.Sql($"ALTER TABLE \"{TableName}\" ALTER COLUMN \"{RowCountColumn}\" DROP DEFAULT;");
            }

            if (!Schema.Table(TableName).Column(FileNameColumn).Exists())
            {
                Alter.Table(TableName)
                    .AddColumn(FileNameColumn).AsCustom("TEXT").NotNullable().WithDefaultValue(string.Empty);
                Execute.Sql($"ALTER TABLE \"{TableName}\" ALTER COLUMN \"{FileNameColumn}\" DROP DEFAULT;");
            }
        }

        Execute.Sql($"ALTER TABLE \"{TableName}\" DROP CONSTRAINT IF EXISTS \"{LegacyUniqueConstraintName}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyUniqueIndexName}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{ShopHashUniqueIndexName}\";");

        Execute.Sql($"""
CREATE UNIQUE INDEX IF NOT EXISTS "{ShopHashUniqueIndexName}"
ON "{TableName}" ("{ShopIdColumn}", "{FileHashColumn}");
""");
    }

    public override void Down()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{ShopHashUniqueIndexName}\";");
    }
}
