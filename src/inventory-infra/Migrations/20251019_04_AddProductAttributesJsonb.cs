using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations
{
  [Migration(2025101904)]
  public sealed class _20251019_04_AddProductAttributesJsonb : Migration
  {
    public override void Up()
    {
      Execute.Sql(@"ALTER TABLE ""Product""
        ADD COLUMN IF NOT EXISTS ""Attributes"" jsonb NOT NULL DEFAULT '{}'::jsonb;");

      Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_product_attributes_gin
        ON ""Product"" USING GIN (""Attributes"");");
    }

    public override void Down()
    {
      Execute.Sql(@"DROP INDEX IF EXISTS ix_product_attributes_gin;");
      // On laisse la colonne en place si elle est utilisée ; si vous tenez à la retirer :
      // Execute.Sql(@"ALTER TABLE ""Product"" DROP COLUMN IF EXISTS ""Attributes"";");
    }
  }
}
