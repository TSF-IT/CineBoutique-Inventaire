using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202510170001)]
public sealed class AddProductAttributesAndGroups : Migration
{
    private const string ProductTable = "Product";
    private const string ProductGroupTable = "ProductGroup";
    private const string AttributesColumn = "Attributes";
    private const string GroupIdColumn = "GroupId";
    private const string AttributesGinIndexName = "ix_product_attributes_gin";
    private const string CodeDigitsIndexName = "ix_product_codedigits";
    private const string EanTrgmIndexName = "ix_product_ean_trgm";
    private const string NameTrgmIndexName = "ix_product_name_trgm";
    private const string SkuTrgmIndexName = "ix_product_sku_trgm";
    private const string ProductGroupParentIndexName = "ix_productgroup_parent";
    private const string ProductGroupLabelTrgmIndexName = "ix_productgroup_label_trgm";
    private const string ProductGroupCodeUniqueIndexName = "ux_productgroup_code";
    private const string ProductGroupForeignKeyName = "fk_product_productgroup_groupid";
    private const string ProductGroupCodeColumn = "Code";
    private const string ProductGroupLabelColumn = "Label";
    private const string ProductGroupParentIdColumn = "ParentId";
    private const string ProductGroupIdColumn = "Id";
    private const string ProductCodeDigitsColumn = "CodeDigits";
    private const string ProductSkuColumn = "Sku";
    private const string ProductEanColumn = "Ean";
    private const string ProductNameColumn = "Name";
    private const string ImmutableUnaccentFunctionName = "immutable_unaccent";

    public override void Up()
    {
        EnsureExtensions();
        EnsureProductGroupTable();
        EnsureProductAttributesColumn();
        EnsureProductGroupColumn();
        CreateSearchIndexes();
    }

    public override void Down()
    {
        DropSearchIndexes();
        DropProductGroupColumn();
        DropProductAttributesColumn();
        DropProductGroupTable();
    }

    private void EnsureExtensions()
    {
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        Execute.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        EnsureImmutableUnaccentFunction();
    }

    private void EnsureProductAttributesColumn()
    {
        if (!Schema.Table(ProductTable).Column(AttributesColumn).Exists())
        {
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ADD COLUMN \"{AttributesColumn}\" JSONB NOT NULL DEFAULT '{{}}'::jsonb;");
        }
        else
        {
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{AttributesColumn}\" SET DATA TYPE JSONB;");
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{AttributesColumn}\" SET DEFAULT '{{}}'::jsonb;");
            Execute.Sql($"UPDATE \"{ProductTable}\" SET \"{AttributesColumn}\" = '{{}}'::jsonb WHERE \"{AttributesColumn}\" IS NULL;");
            Execute.Sql($"ALTER TABLE \"{ProductTable}\" ALTER COLUMN \"{AttributesColumn}\" SET NOT NULL;");
        }

        Execute.Sql($"CREATE INDEX IF NOT EXISTS {AttributesGinIndexName} ON \"{ProductTable}\" USING GIN (\"{AttributesColumn}\");");
    }

    private void EnsureProductGroupTable()
    {
        if (!Schema.Table(ProductGroupTable).Exists())
        {
            Create.Table(ProductGroupTable)
                .WithColumn(ProductGroupIdColumn).AsInt64().PrimaryKey().Identity()
                .WithColumn(ProductGroupCodeColumn).AsCustom("TEXT").Nullable()
                .WithColumn(ProductGroupLabelColumn).AsCustom("TEXT").NotNullable()
                .WithColumn(ProductGroupParentIdColumn).AsInt64().Nullable();
        }
        else if (!Schema.Table(ProductGroupTable).Column(ProductGroupParentIdColumn).Exists())
        {
            Alter.Table(ProductGroupTable)
                .AddColumn(ProductGroupParentIdColumn)
                .AsInt64()
                .Nullable();
        }

        EnsureProductGroupCodeIndex();

        if (Schema.Table(ProductGroupTable).Column(ProductGroupParentIdColumn).Exists())
        {
            if (!Schema.Table(ProductGroupTable).Index(ProductGroupParentIndexName).Exists())
            {
                Create.Index(ProductGroupParentIndexName)
                    .OnTable(ProductGroupTable)
                    .OnColumn(ProductGroupParentIdColumn).Ascending();
            }
        }

        if (Schema.Table(ProductGroupTable).Column(ProductGroupLabelColumn).Exists())
        {
            Execute.Sql($"CREATE INDEX IF NOT EXISTS {ProductGroupLabelTrgmIndexName} ON \"{ProductGroupTable}\" USING GIN ({ImmutableUnaccentFunctionName}(LOWER(\"{ProductGroupLabelColumn}\")) gin_trgm_ops);");
        }

        if (Schema.Table(ProductGroupTable).Exists())
        {
            CreateProductGroupSelfReference();
        }
    }

    private void CreateProductGroupSelfReference()
    {
        if (!Schema.Table(ProductGroupTable).Constraint($"FK_{ProductGroupTable}_{ProductGroupTable}_{ProductGroupParentIdColumn}").Exists())
        {
            Create.ForeignKey($"FK_{ProductGroupTable}_{ProductGroupTable}_{ProductGroupParentIdColumn}")
                .FromTable(ProductGroupTable).ForeignColumn(ProductGroupParentIdColumn)
                .ToTable(ProductGroupTable).PrimaryColumn(ProductGroupIdColumn)
                .OnDeleteOrUpdate(System.Data.Rule.None);
        }
    }

    private void EnsureProductGroupCodeIndex()
    {
        if (Schema.Table(ProductGroupTable).Column(ProductGroupCodeColumn).Exists())
        {
            Execute.Sql($"DROP INDEX IF EXISTS {ProductGroupCodeUniqueIndexName};");
            Execute.Sql($"CREATE UNIQUE INDEX IF NOT EXISTS {ProductGroupCodeUniqueIndexName} ON \"{ProductGroupTable}\" (\"{ProductGroupCodeColumn}\") WHERE \"{ProductGroupCodeColumn}\" IS NOT NULL;");
        }
    }

    private void EnsureProductGroupColumn()
    {
        if (!Schema.Table(ProductTable).Column(GroupIdColumn).Exists())
        {
            Alter.Table(ProductTable)
                .AddColumn(GroupIdColumn)
                .AsInt64()
                .Nullable();
        }

        if (Schema.Table(ProductTable).Column(GroupIdColumn).Exists())
        {
            if (!Schema.Table(ProductTable).Constraint(ProductGroupForeignKeyName).Exists())
            {
                Create.ForeignKey(ProductGroupForeignKeyName)
                    .FromTable(ProductTable).ForeignColumn(GroupIdColumn)
                    .ToTable(ProductGroupTable).PrimaryColumn(ProductGroupIdColumn)
                    .OnDeleteOrUpdate(System.Data.Rule.SetNull);
            }
        }
    }

    private void CreateSearchIndexes()
    {
        Execute.Sql($"CREATE INDEX IF NOT EXISTS {SkuTrgmIndexName} ON \"{ProductTable}\" USING GIN (LOWER(\"{ProductSkuColumn}\") gin_trgm_ops);");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS {EanTrgmIndexName} ON \"{ProductTable}\" USING GIN (LOWER(\"{ProductEanColumn}\") gin_trgm_ops);");
        Execute.Sql($"CREATE INDEX IF NOT EXISTS {NameTrgmIndexName} ON \"{ProductTable}\" USING GIN ({ImmutableUnaccentFunctionName}(LOWER(\"{ProductNameColumn}\")) gin_trgm_ops);");

        if (Schema.Table(ProductTable).Column(ProductCodeDigitsColumn).Exists())
        {
            Execute.Sql($"CREATE INDEX IF NOT EXISTS {CodeDigitsIndexName} ON \"{ProductTable}\" (\"{ProductCodeDigitsColumn}\");");
        }
    }

    private void DropSearchIndexes()
    {
        Execute.Sql($"DROP INDEX IF EXISTS {SkuTrgmIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {EanTrgmIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {NameTrgmIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {CodeDigitsIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {AttributesGinIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {ProductGroupParentIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {ProductGroupCodeUniqueIndexName};");
        Execute.Sql($"DROP INDEX IF EXISTS {ProductGroupLabelTrgmIndexName};");
        DropImmutableUnaccentFunction();
    }

    private void DropProductGroupColumn()
    {
        if (Schema.Table(ProductTable).Column(GroupIdColumn).Exists())
        {
            if (Schema.Table(ProductTable).Constraint(ProductGroupForeignKeyName).Exists())
            {
                Delete.ForeignKey(ProductGroupForeignKeyName).OnTable(ProductTable);
            }

            Delete.Column(GroupIdColumn).FromTable(ProductTable);
        }
    }

    private void DropProductAttributesColumn()
    {
        if (Schema.Table(ProductTable).Column(AttributesColumn).Exists())
        {
            Delete.Column(AttributesColumn).FromTable(ProductTable);
        }
    }

    private void DropProductGroupTable()
    {
        if (Schema.Table(ProductGroupTable).Exists())
        {
            if (Schema.Table(ProductGroupTable).Constraint($"FK_{ProductGroupTable}_{ProductGroupTable}_{ProductGroupParentIdColumn}").Exists())
            {
                Delete.ForeignKey($"FK_{ProductGroupTable}_{ProductGroupTable}_{ProductGroupParentIdColumn}").OnTable(ProductGroupTable);
            }

            Delete.Table(ProductGroupTable);
        }
    }

    private void EnsureImmutableUnaccentFunction()
    {
        Execute.Sql($@"CREATE OR REPLACE FUNCTION {ImmutableUnaccentFunctionName}(text)
RETURNS text
LANGUAGE sql
IMMUTABLE
STRICT
AS $$
SELECT unaccent('public.unaccent', $1);
$$;");
    }

    private void DropImmutableUnaccentFunction()
    {
        Execute.Sql($"DROP FUNCTION IF EXISTS {ImmutableUnaccentFunctionName}(text);");
    }
}
