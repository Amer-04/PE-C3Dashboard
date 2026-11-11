-- C3 Site Finder: step 4 of 4, lock the migration role after the import. Run in the SQL Editor as postgres.
-- c3_migrator can no longer log in, create anything or even look inside schema c3. It stays the owner of the c3
-- tables, so a later refresh only needs: ALTER ROLE c3_migrator LOGIN; GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;
REVOKE ALL ON SCHEMA c3 FROM c3_migrator;
-- Exception, only while shared methodology editing is installed (role c3_methodology_writer exists): PostgreSQL runs
-- the foreign-key checks of the writer's inserts/updates/deletes AS THE TABLE OWNER, which therefore needs USAGE on
-- schema c3 (without it every methodology write fails with "permission denied for schema c3"). c3_migrator still
-- cannot log in or create anything, so this lets nobody act as it.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'c3_methodology_writer') THEN
    GRANT USAGE ON SCHEMA c3 TO c3_migrator;
  END IF;
END $$;
ALTER ROLE c3_migrator NOLOGIN;
