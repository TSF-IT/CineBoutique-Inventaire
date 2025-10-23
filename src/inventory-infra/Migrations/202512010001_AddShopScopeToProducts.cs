using System;
using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202512010001)]
public sealed class AddShopScopeToProducts : Migration
{
    private const string ProductTable = "Product";
    private const string ProductShopIdColumn = "ShopId";
    private const string ProductSkuColumn = "Sku";
    private const string ProductEanColumn = "Ean";
    private const string ProductCodeDigitsColumn = "CodeDigits";

    private const string ShopTable = "Shop";
    private const string ShopIdColumn = "Id";

    private const string ProductImportHistoryTable = "ProductImportHistory";
    private const string ProductImportHistoryShopIdColumn = "ShopId";

    private const string ProductShopForeignKey = "FK_Product_Shop_ShopId";
    private const string ProductShopSkuIndex = "UX_Product_Shop_LowerSku";
    private const string ProductShopEanIndex = "UX_Product_Shop_Ean_NotNull";
    private const string ProductShopDigitsIndex = "IX_Product_Shop_CodeDigits";
    private const string LegacySkuIndex = "UX_Product_LowerSku";
    private const string LegacyEanIndex = "UX_Product_Ean_NotNull";
    private const string LegacyDigitsIndex = "IX_Product_CodeDigits";
    private const string ProductImportHistoryShopIndex = "IX_ProductImportHistory_ShopId";

    public override void Up()
    {
        EnsureProductTableIsEmpty();
        EnsureProductShopColumn();
        EnsureProductForeignKey();
        RebuildProductIndexes();
        EnsureProductImportHistoryShopColumn();
    }

    public override void Down()
    {
        DropProductImportHistoryShopColumn();
        DropProductIndexes();
        DropProductForeignKey();
        DropProductShopColumn();
    }

    private void EnsureProductTableIsEmpty()
    {
        Execute.Sql(@"DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM ""Product"") THEN
        RAISE EXCEPTION 'AddShopScopeToProducts requires an empty ""Product"" table.';
    END IF;
END
$$;");
    }

    private void EnsureProductShopColumn()
    {
        if (!Schema.Table(ProductTable).Column(ProductShopIdColumn).Exists())
        {
            Alter.Table(ProductTable)
                .AddColumn(ProductShopIdColumn)
                .AsGuid()
                .NotNullable();
        }
    }

    private void DropProductShopColumn()
    {
        if (Schema.Table(ProductTable).Column(ProductShopIdColumn).Exists())
        {
            Delete.Column(ProductShopIdColumn).FromTable(ProductTable);
        }
    }

    private void EnsureProductForeignKey()
    {
        if (!Schema.Table(ProductTable).Constraint(ProductShopForeignKey).Exists())
        {
            Create.ForeignKey(ProductShopForeignKey)
                .FromTable(ProductTable).ForeignColumn(ProductShopIdColumn)
                .ToTable(ShopTable).PrimaryColumn(ShopIdColumn)
                .OnDeleteOrUpdate(System.Data.Rule.Cascade);
        }
    }

    private void DropProductForeignKey()
    {
        if (Schema.Table(ProductTable).Constraint(ProductShopForeignKey).Exists())
        {
            Delete.ForeignKey(ProductShopForeignKey).OnTable(ProductTable);
        }
    }

    private void RebuildProductIndexes()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacySkuIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyEanIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{LegacyDigitsIndex}\";");

        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{ProductShopSkuIndex}\" ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", LOWER(\"{ProductSkuColumn}\"));");
        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{ProductShopEanIndex}\" ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductEanColumn}\") WHERE \"{ProductEanColumn}\" IS NOT NULL;");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS \"{ProductShopDigitsIndex}\" ON \"{ProductTable}\" (\"{ProductShopIdColumn}\", \"{ProductCodeDigitsColumn}\");");
    }

    private void DropProductIndexes()
    {
        Execute.Sql($"DROP INDEX IF EXISTS \"{ProductShopSkuIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{ProductShopEanIndex}\";");
        Execute.Sql($"DROP INDEX IF EXISTS \"{ProductShopDigitsIndex}\";");

        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{LegacySkuIndex}\" ON \"{ProductTable}\" ((LOWER(\"{ProductSkuColumn}\")));");
        Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS \"{LegacyEanIndex}\" ON \"{ProductTable}\" (\"{ProductEanColumn}\") WHERE \"{ProductEanColumn}\" IS NOT NULL;");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS \"{LegacyDigitsIndex}\" ON \"{ProductTable}\" (\"{ProductCodeDigitsColumn}\");");
    }

    private void EnsureProductImportHistoryShopColumn()
    {
        if (!Schema.Table(ProductImportHistoryTable).Exists())
        {
            return;
        }

        if (!Schema.Table(ProductImportHistoryTable).Column(ProductImportHistoryShopIdColumn).Exists())
        {
            Alter.Table(ProductImportHistoryTable)
                .AddColumn(ProductImportHistoryShopIdColumn)
                .AsGuid()
                .Nullable();

            Execute.Sql($"UPDATE \"{ProductImportHistoryTable}\" SET \"{ProductImportHistoryShopIdColumn}\" = '00000000-0000-0000-0000-000000000000' WHERE \"{ProductImportHistoryShopIdColumn}\" IS NULL;");
            Execute.Sql($"ALTER TABLE \"{ProductImportHistoryTable}\" ALTER COLUMN \"{ProductImportHistoryShopIdColumn}\" SET NOT NULL;");
        }

        if (!Schema.Table(ProductImportHistoryTable).Index(ProductImportHistoryShopIndex).Exists())
        {
            Create.Index(ProductImportHistoryShopIndex)
                .OnTable(ProductImportHistoryTable)
                .OnColumn(ProductImportHistoryShopIdColumn).Ascending();
        }
    }

    private void DropProductImportHistoryShopColumn()
    {
        if (!Schema.Table(ProductImportHistoryTable).Exists())
        {
            return;
        }

        if (Schema.Table(ProductImportHistoryTable).Index(ProductImportHistoryShopIndex).Exists())
        {
            Delete.Index(ProductImportHistoryShopIndex).OnTable(ProductImportHistoryTable);
        }

        if (Schema.Table(ProductImportHistoryTable).Column(ProductImportHistoryShopIdColumn).Exists())
        {
            Delete.Column(ProductImportHistoryShopIdColumn).FromTable(ProductImportHistoryTable);
        }
    }
}
