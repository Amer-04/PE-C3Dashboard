-- C3 Site Finder: ROLLBACK of step A (removes the methodology writer login). Run in the SQL Editor as postgres, AFTER
-- "NJTruckCharging.CloudCopy methodology-editing-rollback"; then run 04-lockdown.sql. Idempotent. Touches no data.
-- (For an immediate stop without any of this, "ALTER ROLE c3_methodology_writer NOLOGIN;" is enough; see the guide.)
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'c3_methodology_writer') THEN
    -- End its open sessions first (the API should already run without it).
    PERFORM pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = 'c3_methodology_writer';
    REVOKE ALL ON SCHEMA c3 FROM c3_methodology_writer;
    DROP ROLE c3_methodology_writer;
  END IF;
END $$;
