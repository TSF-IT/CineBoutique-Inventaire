using FluentMigrator;

namespace CineBoutique.Inventory.Infrastructure.Migrations
{
    // Assure-toi que ce numéro est > à vos migrations existantes (format 12 chiffres)
    [Migration(202510200002)]
    public sealed class _20251020_0002_AddShopKindAndSeedLumiere : Migration
    {
        public override void Up()
        {
            // Tout en SQL idempotent pour éviter les soucis d'APIs FluentMigrator
            Execute.Sql(@"
DO $$
BEGIN
  -- 1) Colonne Kind (si absente)
  IF to_regclass('public.""Shop""') IS NOT NULL THEN
    IF NOT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_schema='public' AND table_name='Shop' AND column_name='Kind'
    ) THEN
      ALTER TABLE ""Shop"" ADD COLUMN ""Kind"" text NOT NULL DEFAULT 'boutique';
    END IF;

    -- 2) Contrainte sur les valeurs autorisées
    IF NOT EXISTS (
      SELECT 1 FROM pg_constraint WHERE conname = 'ck_shop_kind_allowed'
    ) THEN
      ALTER TABLE ""Shop""
        ADD CONSTRAINT ck_shop_kind_allowed
        CHECK (lower(""Kind"") IN ('boutique','lumiere','camera'));
    END IF;

    -- 3) Index pour les filtres par type
    CREATE INDEX IF NOT EXISTS ix_shop_kind ON ""Shop"" (lower(""Kind""));

    -- 4) Renommages demandés
    UPDATE ""Shop"" SET ""Name""='CinéBoutique Saint-Denis' WHERE ""Name""='CinéBoutique Paris';
    UPDATE ""Shop"" SET ""Name""='CinéBoutique Belgique'    WHERE ""Name""='CinéBoutique Bruxelles';

    -- 5) Ajout des entités Lumière (idempotent par Name)
    INSERT INTO ""Shop"" (""Name"", ""Kind"")
    SELECT v.name, v.kind
    FROM (VALUES
      ('Lumière Saint-Denis','lumiere'),
      ('Lumière Marseille','lumiere'),
      ('Lumière Montpellier','lumiere'),
      ('Lumière Bordeaux','lumiere'),
      ('Lumière Belgique','lumiere')
    ) AS v(name,kind)
    WHERE NOT EXISTS (SELECT 1 FROM ""Shop"" s WHERE s.""Name"" = v.name);
  END IF;
END $$;
");
        }

        public override void Down()
        {
            // On ne supprime pas les nouvelles entités en down.
            // On retire l'index, la contrainte et la colonne pour revenir à l'état antérieur si nécessaire.
            Execute.Sql(@"
DROP INDEX IF EXISTS ix_shop_kind;
ALTER TABLE ""Shop"" DROP CONSTRAINT IF EXISTS ck_shop_kind_allowed;
DO $$
BEGIN
  IF to_regclass('public.""Shop""') IS NOT NULL THEN
    IF EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_schema='public' AND table_name='Shop' AND column_name='Kind'
    ) THEN
      ALTER TABLE ""Shop"" DROP COLUMN ""Kind"";
    END IF;
  END IF;
END $$;
");
        }
    }
}
