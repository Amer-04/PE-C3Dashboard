-- C3 Site Finder: step 4 of 4, lock the migration role after the import. Run in the SQL Editor as postgres.
-- c3_migrator can no longer log in or create schemas; it stays the owner of c3 so it can be re-enabled for a refresh.
DO $$ BEGIN EXECUTE format('REVOKE CREATE ON DATABASE %I FROM c3_migrator', current_database()); END $$;
ALTER ROLE c3_migrator NOLOGIN;
