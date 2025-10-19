using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations
{
  [Migration(202510200001)]
  public sealed class _20251020_0001_AddProductAttributesJsonb : Migration
  {
    public override void Up()
    {
      // Ceinture et bretelles: n'agit que si la table existe (élimine 42P01 si ordre inattendu)
      Execute.Sql(@"
DO $$
BEGIN
  IF to_regclass('public.""Product""') IS NOT NULL THEN
    ALTER TABLE ""Product""
      ADD COLUMN IF NOT EXISTS ""Attributes"" jsonb NOT NULL DEFAULT '{}'::jsonb;
    CREATE INDEX IF NOT EXISTS ix_product_attributes_gin
      ON ""Product"" USING GIN (""Attributes"");
  END IF;
END $$;");
    }

    public override void Down()
    {
      Execute.Sql(@"DROP INDEX IF EXISTS ix_product_attributes_gin;");
      // Facultatif : si vous voulez vraiment la retirer lors d'un down
      // Execute.Sql(@"ALTER TABLE IF EXISTS ""Product"" DROP COLUMN IF EXISTS ""Attributes"";");
    }
  }
}
