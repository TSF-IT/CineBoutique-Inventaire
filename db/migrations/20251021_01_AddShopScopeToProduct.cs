using System.Data;
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202510210001)]
public sealed class AddShopScopeToProduct : Migration
{
    private const string ProductTable = "Product";
    private const string ProductSkuColumn = "Sku";
    private const string ProductEanColumn = "Ean";
    private const string ProductCodeDigitsColumn = "CodeDigits";
    private const string ProductShopIdColumn = "ShopId";

    private const string ShopTable = "Shop";
    private const string ShopIdColumn = "Id";

    private const string ProductImportTable = "ProductImport";
    private const string ProductImportIdColumn = "Id";
    private const string ProductImportShopIdColumn = "ShopId";
    private const string ProductImportFileNameColumn = "FileName";
    private const string ProductImportFileHashColumn = "FileHashSha256";
    private const string ProductImportRowCountColumn = "RowCount";
    private const string ProductImportImportedAtColumn = "ImportedAtUtc";

    private const string ProductShopForeignKeyName = "fk_product_shop_shopid";
    private const string ProductImportShopForeignKeyName = "fk_productimport_shop_shopid";
    private const string ProductImportShopUniqueConstraintName = "uq_productimport_shopid";
    private const string ProductImportShopFileHashUniqueIndexName = "ux_productimport_shopid_filehash";

    private const string ProductShopSkuUniqueIndexName = "ux_product_shopid_sku";
    private const string ProductShopEanUniqueIndexName = "ux_product_shopid_ean";
    private const string ProductShopCodeDigitsIndexName = "ix_product_shopid_codedigits";
    private const string LegacyProductCodeDigitsIndexName = "IX_Product_CodeDigits";

    public override void Up()
    {
        EnsureProductTableIsEmpty();
        EnsureProductShopIdColumn();
        EnsureProductShopForeignKey();
        RebuildProductIndexes();
        CreateProductImportTable();
    }

    public override void Down()
    {
        DropProductImportTable();
        DropProductIndexes();
        DropProductShopForeignKey();
        DropProductShopIdColumn();
    }

    private void EnsureProductTableIsEmpty()
    {
        Execute.Sql(
            @"DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""Product"") THEN
        RAISE EXCEPTION 'Migration AddShopScopeToProduct requires an empty ""Product"" table.';
    END IF;
END
$$;");
    }

    private void EnsureProductShopIdColumn()
    {
        if (!Schema.Table(ProductTable).Column(ProductShopIdColumn).Exists())
        {
            Alter.Table(ProductTable)
                .AddColumn(ProductShopIdColumn)
                .AsGuid()
                .NotNullable();
        }
        else
        {
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{ProductShopIdColumn}\" SET DATA TYPE uuid;");
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{ProductShopIdColumn}\" SET NOT NULL;");
        }
    }

    private void EnsureProductShopForeignKey()
    {
        if (!Schema.Table(ProductTable).Constraint(ProductShopForeignKeyName).Exists())
        {
            Create.ForeignKey(ProductShopForeignKeyName)
                .FromTable(ProductTable).ForeignColumn(ProductShopIdColumn)
                .ToTable(ShopTable).PrimaryColumn(ShopIdColumn)
                .OnDeleteOrUpdate(Rule.Cascade);
        }
    }

    private void RebuildProductIndexes()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyProductCodeDigitsIndexName}\";");
        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS {ProductShopSkuUniqueIndexName} ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductSkuColumn}\") WHERE \"{ProductSkuColumn}\" IS NOT NULL;");
        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS {ProductShopEanUniqueIndexName} ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductEanColumn}\") WHERE \"{ProductEanColumn}\" IS NOT NULL;");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS {ProductShopCodeDigitsIndexName} ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductCodeDigitsColumn}\");");
    }

    private void CreateProductImportTable()
    {
        if (!Schema.Table(ProductImportTable).Exists())
        {
            Create.Table(ProductImportTable)
                .WithColumn(ProductImportIdColumn).AsGuid().PrimaryKey()
                .WithColumn(ProductImportShopIdColumn).AsGuid().NotNullable()
                .WithColumn(ProductImportFileNameColumn).AsCustom("TEXT").NotNullable()
                .WithColumn(ProductImportFileHashColumn).AsCustom("CHAR(64)").NotNullable()
                .WithColumn(ProductImportRowCountColumn).AsInt32().NotNullable()
                .WithColumn(ProductImportImportedAtColumn).AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

            Create.UniqueConstraint(ProductImportShopUniqueConstraintName)
                .OnTable(ProductImportTable)
                .Column(ProductImportShopIdColumn);

            Create.Index(ProductImportShopFileHashUniqueIndexName)
                .OnTable(ProductImportTable)
                .OnColumn(ProductImportShopIdColumn).Ascending()
                .OnColumn(ProductImportFileHashColumn).Ascending()
                .WithOptions().Unique();

            Create.ForeignKey(ProductImportShopForeignKeyName)
                .FromTable(ProductImportTable).ForeignColumn(ProductImportShopIdColumn)
                .ToTable(ShopTable).PrimaryColumn(ShopIdColumn)
                .OnDeleteOrUpdate(Rule.Cascade);
        }
    }

    private void DropProductImportTable()
    {
        if (Schema.Table(ProductImportTable).Exists())
        {
            Delete.Table(ProductImportTable);
        }
    }

    private void DropProductIndexes()
    {
        Execute.Sql($"DROP INDEX IF EXISTS {ProductShopSkuUniqueIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {ProductShopEanUniqueIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {ProductShopCodeDigitsIndexName};");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS \"{LegacyProductCodeDigitsIndexName}\" ON \"{ProductTable}\" (\"{ProductCodeDigitsColumn}\");");
    }

    private void DropProductShopForeignKey()
    {
        if (Schema.Table(ProductTable).Constraint(ProductShopForeignKeyName).Exists())
        {
            Delete.ForeignKey(ProductShopForeignKeyName).OnTable(ProductTable);
        }
    }

    private void DropProductShopIdColumn()
    {
        if (Schema.Table(ProductTable).Column(ProductShopIdColumn).Exists())
        {
            Delete.Column(ProductShopIdColumn).FromTable(ProductTable);
        }
    }
}
