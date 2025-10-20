using FluentMigrator;

namespace Inventory.Infra.Migrations;

[Migration(202510200001)]
public class _202510200001_AddShopKindAndSeedLumiere : Migration
{
  public override void Up()
  {
    if (!Schema.Table("Shop").Column("Kind").Exists())
    {
      Alter.Table("Shop").AddColumn("Kind").AsString().NotNullable().WithDefaultValue("boutique");
    }

    // Contrainte "Kind" autorisée
    Execute.Sql(@"
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'ck_shop_kind_allowed'
  ) THEN
    ALTER TABLE ""Shop""
      ADD CONSTRAINT ck_shop_kind_allowed
      CHECK (lower(""Kind"") IN ('boutique','lumiere','camera'));
  END IF;
END $$;
");

    // Index
    Execute.Sql(@"CREATE INDEX IF NOT EXISTS ix_shop_kind ON ""Shop"" (lower(""Kind""));");

    // Renommages
    Execute.Sql(@"UPDATE ""Shop"" SET ""Name""='CinéBoutique Saint-Denis' WHERE ""Name""='CinéBoutique Paris';");
    Execute.Sql(@"UPDATE ""Shop"" SET ""Name""='CinéBoutique Belgique'    WHERE ""Name""='CinéBoutique Bruxelles';");

    // Ajout Lumière idempotent
    Execute.Sql(@"
INSERT INTO ""Shop"" (""Name"",""Kind"")
SELECT v.name, v.kind
FROM (VALUES
  ('Lumière Saint-Denis','lumiere'),
  ('Lumière Marseille','lumiere'),
  ('Lumière Montpellier','lumiere'),
  ('Lumière Bordeaux','lumiere'),
  ('Lumière Belgique','lumiere')
) AS v(name,kind)
WHERE NOT EXISTS (SELECT 1 FROM ""Shop"" s WHERE s.""Name"" = v.name);
");
  }

  public override void Down()
  {
    // On ne supprime pas les nouvelles entités ; on retire l'index & contrainte, et la colonne si besoin.
    Execute.Sql(@"DROP INDEX IF EXISTS ix_shop_kind;");
    Execute.Sql(@"ALTER TABLE ""Shop"" DROP CONSTRAINT IF EXISTS ck_shop_kind_allowed;");
    if (Schema.Table("Shop").Column("Kind").Exists())
    {
      Delete.Column("Kind").FromTable("Shop");
    }
  }
}
