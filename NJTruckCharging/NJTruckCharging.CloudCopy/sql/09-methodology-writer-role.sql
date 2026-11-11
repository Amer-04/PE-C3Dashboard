-- C3 Site Finder: shared methodology editing, step A of 3. Run ONCE in the SQL Editor as postgres.
-- Use the copy printed by "NJTruckCharging.CloudCopy writer-bootstrap-sql": it fills in the password as a
-- SCRAM-SHA-256 verifier (a one-way hash), so the plain password is never typed, stored or logged here.
--
-- Creates ONE new login, c3_methodology_writer, used by the API only for methodology changes. It gets nothing yet:
-- its exact table and column permissions are granted by the table owner in step B (10-methodology-editing.sql).
-- Also re-opens c3_migrator for step B (the documented refresh procedure); step C (04-lockdown.sql) closes it again.
-- Changes nothing else: c3_runtime_readonly, the c3 data, other schemas and project settings are untouched.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'c3_methodology_writer') THEN
    RAISE EXCEPTION 'Role c3_methodology_writer already exists - stopping without changes.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'c3') THEN
    RAISE EXCEPTION 'Schema c3 does not exist - stopping without changes.';
  END IF;
END $$;

-- Writer login: no superuser/createdb/createrole/bypassrls, at most 3 connections, short timeouts, NOT read-only.
CREATE ROLE c3_methodology_writer LOGIN NOINHERIT NOCREATEDB NOCREATEROLE NOBYPASSRLS CONNECTION LIMIT 3 PASSWORD '{{WRITER_VERIFIER}}';
ALTER ROLE c3_methodology_writer SET search_path = c3;
ALTER ROLE c3_methodology_writer SET statement_timeout = '120s';
ALTER ROLE c3_methodology_writer SET lock_timeout = '10s';
ALTER ROLE c3_methodology_writer SET idle_in_transaction_session_timeout = '60s';
-- May look inside schema c3 (table permissions come in step B); may not create anything.
GRANT USAGE ON SCHEMA c3 TO c3_methodology_writer;

-- Step B runs as the table owner: re-open c3_migrator (closed again by 04-lockdown.sql right after step B).
ALTER ROLE c3_migrator LOGIN;
GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;
