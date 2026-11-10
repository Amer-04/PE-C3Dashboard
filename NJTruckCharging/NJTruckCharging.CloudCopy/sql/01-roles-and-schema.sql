-- C3 Site Finder: step 1 of 4, dedicated roles. Run ONCE in the SQL Editor as postgres.
-- Use the copy printed by "NJTruckCharging.CloudCopy bootstrap-sql": it fills the two {{...}} placeholders with
-- SCRAM-SHA-256 password verifiers (one-way hashes), so no plain password is ever typed, stored or logged.
-- Creates only: role c3_migrator (builds and fills schema c3) and role c3_runtime_readonly (the API's read-only login).
-- Touches nothing else: no existing schema, table, role or project setting is changed.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'c3') THEN
    RAISE EXCEPTION 'Schema c3 already exists - stopping without changes.';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname IN ('c3_migrator', 'c3_runtime_readonly')) THEN
    RAISE EXCEPTION 'A c3_* role already exists - stopping without changes.';
  END IF;
END $$;

-- Migration role: may create schema c3 (until step 4) and owns only what it creates there. No other privileges.
CREATE ROLE c3_migrator LOGIN NOINHERIT NOCREATEDB NOCREATEROLE CONNECTION LIMIT 3 PASSWORD '{{MIGRATOR_VERIFIER}}';
ALTER ROLE c3_migrator SET search_path = c3;
ALTER ROLE c3_migrator SET statement_timeout = '10min';

-- Runtime role used by the Render API: SELECT on c3 only (granted in step 3), every transaction read-only.
CREATE ROLE c3_runtime_readonly LOGIN NOINHERIT NOCREATEDB NOCREATEROLE CONNECTION LIMIT 10 PASSWORD '{{READONLY_VERIFIER}}';
ALTER ROLE c3_runtime_readonly SET default_transaction_read_only = on;
ALTER ROLE c3_runtime_readonly SET search_path = c3;
ALTER ROLE c3_runtime_readonly SET statement_timeout = '60s';
ALTER ROLE c3_runtime_readonly SET idle_in_transaction_session_timeout = '30s';

-- Let c3_migrator create its own schema (revoked again in step 4).
DO $$ BEGIN EXECUTE format('GRANT CREATE ON DATABASE %I TO c3_migrator', current_database()); END $$;
