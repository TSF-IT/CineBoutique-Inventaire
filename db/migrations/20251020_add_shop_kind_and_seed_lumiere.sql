-- 20251020_add_shop_kind_and_seed_lumiere.sql
-- 1) Colonne Kind (type d'entité) sur Shop
ALTER TABLE "Shop"
  ADD COLUMN IF NOT EXISTS "Kind" text NOT NULL DEFAULT 'boutique';

-- 2) Contrainte simple (extensible) : 'boutique' | 'lumiere' | 'camera'
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'ck_shop_kind_allowed'
  ) THEN
    ALTER TABLE "Shop"
      ADD CONSTRAINT ck_shop_kind_allowed
      CHECK (lower("Kind") IN ('boutique','lumiere','camera'));
  END IF;
END $$;

-- 3) Index utile pour filtres par type
CREATE INDEX IF NOT EXISTS ix_shop_kind ON "Shop" (lower("Kind"));

-- 4) Renommages demandés
UPDATE "Shop" SET "Name" = 'CinéBoutique Saint-Denis' WHERE "Name" = 'CinéBoutique Paris';
UPDATE "Shop" SET "Name" = 'CinéBoutique Belgique'    WHERE "Name" = 'CinéBoutique Bruxelles';

-- 5) Ajout des entités Lumière (idempotent par Name)
INSERT INTO "Shop" ("Name","Kind")
SELECT v.name, v.kind
FROM (VALUES
  ('Lumière Saint-Denis','lumiere'),
  ('Lumière Marseille','lumiere'),
  ('Lumière Montpellier','lumiere'),
  ('Lumière Bordeaux','lumiere'),
  ('Lumière Belgique','lumiere')
) AS v(name,kind)
WHERE NOT EXISTS (SELECT 1 FROM "Shop" s WHERE s."Name" = v.name);
