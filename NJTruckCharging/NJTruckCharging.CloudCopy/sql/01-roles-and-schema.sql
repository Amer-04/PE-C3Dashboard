-- C3 Site Finder: step 1 of 4, dedicated roles. Run ONCE in the SQL Editor as postgres.
-- Use the copy printed by "NJTruckCharging.CloudCopy bootstrap-sql": it fills the two {{...}} placeholders with
-- SCRAM-SHA-256 password verifiers (one-way hashes), so no plain password is ever typed, stored or logged.
-- Creates only: role c3_migrator (builds and fills the tables in schema c3), role c3_runtime_readonly (the API's
-- read-only login) and schema c3 itself, which stays owned by the administrator running this script.
-- c3_migrator gets USAGE + CREATE on schema c3 only (no database-level CREATE): it can create tables inside c3 and
-- nowhere else. Touches nothing else: no existing schema, table, role or project setting is changed.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'c3') THEN
    RAISE EXCEPTION 'Schema c3 already exists - stopping without changes.';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname IN ('c3_migrator', 'c3_runtime_readonly')) THEN
    RAISE EXCEPTION 'A c3_* role already exists - stopping without changes.';
  END IF;
END $$;

-- Migration role: may create tables inside schema c3 (until step 4) and owns only those tables. No other privileges.
CREATE ROLE c3_migrator LOGIN NOINHERIT NOCREATEDB NOCREATEROLE CONNECTION LIMIT 3 PASSWORD '{{MIGRATOR_VERIFIER}}';
ALTER ROLE c3_migrator SET search_path = c3;
ALTER ROLE c3_migrator SET statement_timeout = '10min';

-- Runtime role used by the Render API: SELECT on c3 only (granted in step 3), every transaction read-only.
CREATE ROLE c3_runtime_readonly LOGIN NOINHERIT NOCREATEDB NOCREATEROLE CONNECTION LIMIT 10 PASSWORD '{{READONLY_VERIFIER}}';
ALTER ROLE c3_runtime_readonly SET default_transaction_read_only = on;
ALTER ROLE c3_runtime_readonly SET search_path = c3;
ALTER ROLE c3_runtime_readonly SET statement_timeout = '60s';
ALTER ROLE c3_runtime_readonly SET idle_in_transaction_session_timeout = '30s';

-- Schema c3, owned by the administrator. Nobody gets anything by default (PUBLIC and Supabase's API roles revoked);
-- c3_migrator may create tables in it until step 4; c3_runtime_readonly may look inside (table SELECT comes in step 3).
CREATE SCHEMA c3;
REVOKE ALL ON SCHEMA c3 FROM PUBLIC;
DO $$
DECLARE r record;
BEGIN
  FOR r IN SELECT rolname FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'service_role', 'authenticator') LOOP
    EXECUTE format('REVOKE ALL ON SCHEMA c3 FROM %I', r.rolname);
  END LOOP;
END $$;
GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;
GRANT USAGE ON SCHEMA c3 TO c3_runtime_readonly;
