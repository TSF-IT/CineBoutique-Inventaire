using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations;

[Migration(2025102303)]
public sealed class AddProductDescription : Migration
{
    public override void Up()
    {
        Execute.Sql(@"
DO $$
BEGIN
  IF to_regclass('public.""Product""') IS NOT NULL THEN
    ALTER TABLE ""Product""
      ADD COLUMN IF NOT EXISTS ""Description"" text NULL;
  END IF;
END $$;");
    }
    public override void Down()
    {
        // Optionnel : ALTER TABLE "Product" DROP COLUMN "Description";
    }
}
