using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(202510190101)]
public sealed class AddTrgmUnaccentAndAutocompleteIndexes : Migration
{
    public override void Up()
    {
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        Execute.Sql(@"CREATE EXTENSION IF NOT EXISTS unaccent;");

        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_sku_trgm
  ON ""Product"" USING GIN (LOWER(""Sku"") gin_trgm_ops);");

        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_ean_trgm
  ON ""Product"" USING GIN (LOWER(""Ean"") gin_trgm_ops);");

        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_name_trgm
  ON ""Product"" USING GIN (unaccent(LOWER(""Name"")) gin_trgm_ops);");

        Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_productgroup_label_trgm
  ON ""ProductGroup"" USING GIN (unaccent(LOWER(""Label"")) gin_trgm_ops);");
    }

    public override void Down()
    {
        Execute.Sql(@"DROP INDEX IF EXISTS ix_productgroup_label_trgm;");
        Execute.Sql(@"DROP INDEX IF EXISTS ix_product_name_trgm;");
        Execute.Sql(@"DROP INDEX IF EXISTS ix_product_ean_trgm;");
        Execute.Sql(@"DROP INDEX IF EXISTS ix_product_sku_trgm;");
    }
}
