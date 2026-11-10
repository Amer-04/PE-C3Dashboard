-- C3 Site Finder: step 4 of 4, lock the migration role after the import. Run in the SQL Editor as postgres.
-- c3_migrator can no longer log in, create anything or even look inside schema c3. It stays the owner of the c3
-- tables, so a later refresh only needs: ALTER ROLE c3_migrator LOGIN; GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;
REVOKE ALL ON SCHEMA c3 FROM c3_migrator;
ALTER ROLE c3_migrator NOLOGIN;
