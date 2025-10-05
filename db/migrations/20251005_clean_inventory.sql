-- Migration: Nettoyage et harmonisation de l'inventaire
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'run_status') THEN
    CREATE TYPE run_status AS ENUM ('not_started','in_progress','completed');
  END IF;
END$$;

UPDATE counting_status
SET owner_user_id = NULL
WHERE owner_user_id IS NOT NULL
  AND owner_user_id !~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$';

ALTER TABLE counting_status
  ALTER COLUMN owner_user_id TYPE uuid USING owner_user_id::uuid,
  ALTER COLUMN status TYPE run_status USING status::run_status,
  ALTER COLUMN started_at_utc TYPE timestamptz,
  ALTER COLUMN completed_at_utc TYPE timestamptz;

ALTER TABLE location
  ALTER COLUMN active_run_id TYPE uuid USING NULLIF(active_run_id, '')::uuid,
  ALTER COLUMN active_count_type TYPE smallint,
  ALTER COLUMN active_started_at_utc TYPE timestamptz;

ALTER TABLE counting_status
  ADD CONSTRAINT IF NOT EXISTS fk_count_status_owner FOREIGN KEY (owner_user_id) REFERENCES shop_user(id);

CREATE OR REPLACE FUNCTION check_same_shop() RETURNS trigger AS $$
BEGIN
  IF NEW.owner_user_id IS NULL THEN RETURN NEW; END IF;

  PERFORM 1 FROM shop_user su
  JOIN location l ON l.id = NEW.location_id
  WHERE su.id = NEW.owner_user_id AND su.shop_id = l.shop_id;

  IF NOT FOUND THEN
    RAISE EXCEPTION 'owner_user_id doit appartenir à la même boutique que la location';
  END IF;

  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_count_status_same_shop ON counting_status;
CREATE TRIGGER trg_count_status_same_shop
BEFORE INSERT OR UPDATE ON counting_status
FOR EACH ROW EXECUTE FUNCTION check_same_shop();
