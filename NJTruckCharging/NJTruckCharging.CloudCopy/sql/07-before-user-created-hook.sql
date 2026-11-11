-- C3 Site Finder: Supabase Auth "Before User Created" hook - only exact company e-mail domains may create an account.
-- PREPARED, NOT APPLIED. Run in the SQL Editor as postgres only after approval (docs/deploy/EMPLOYEE_SELF_REGISTRATION.md,
-- phase 3). Then enable it: Authentication -> Hooks -> Before User Created -> Postgres -> c3_auth.before_user_created.
--
-- What it does: Supabase Auth calls this function before it inserts ANY new user (sign-up, and - to be confirmed in
-- the staging check - admin-created and OAuth users). It allows the user only when the e-mail address has exactly one
-- '@', a non-empty local part, printable ASCII only, and a domain that is EXACTLY poweredison.com or ev-edison.com
-- (case-insensitive). Subdomains, lookalikes, Unicode homoglyphs, trailing dots and phone-only/anonymous users are
-- refused. Same rule as the API (EmployeeEmailPolicy.cs) and the website; all three are tested against
-- NJTruckCharging.Web/src/app/core/employee-email.vectors.json (sql/tests/test-07-hook.mjs).
--
-- This hook is a gate for CREATING accounts. It is not what grants access: the API checks every request itself
-- (signature, expiry, confirmed e-mail, domain). It also does not run when an existing user changes their e-mail
-- address - the API refuses such accounts because it re-checks the current, confirmed address on every request.
--
-- Creates only: schema c3_auth (not exposed by the Data API) and one function in it. Execute rights: only
-- supabase_auth_admin (the role Supabase Auth runs hooks as). Touches no table, no existing role and no setting.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'c3_auth' AND p.proname = 'before_user_created') THEN
    RAISE EXCEPTION 'c3_auth.before_user_created already exists - stopping without changes.';
  END IF;
END $$;

CREATE SCHEMA IF NOT EXISTS c3_auth;
REVOKE ALL ON SCHEMA c3_auth FROM PUBLIC;

CREATE FUNCTION c3_auth.before_user_created(event jsonb)
RETURNS jsonb
LANGUAGE plpgsql
IMMUTABLE
SECURITY INVOKER
SET search_path = ''
AS $$
DECLARE
  email  text := event -> 'user' ->> 'email';
  at_pos int;
  refuse constant jsonb := jsonb_build_object('error', jsonb_build_object(
    'http_code', 403,
    'message', 'Please use your Power Edison or EV Edison work e-mail address.'));
BEGIN
  IF email IS NULL OR length(email) = 0 OR length(email) > 254 THEN RETURN refuse; END IF;
  -- Printable ASCII only (0x21-0x7E): no spaces, control characters or Unicode lookalikes.
  IF email !~ '^[!-~]+$' THEN RETURN refuse; END IF;
  -- Exactly one '@' with something on both sides.
  IF length(email) - length(replace(email, '@', '')) <> 1 THEN RETURN refuse; END IF;
  at_pos := strpos(email, '@');
  IF at_pos = 1 OR at_pos = length(email) THEN RETURN refuse; END IF;
  -- Exact domain match, case-insensitive (lower() on ASCII only, checked above).
  IF lower(substr(email, at_pos + 1)) NOT IN ('poweredison.com', 'ev-edison.com') THEN RETURN refuse; END IF;
  RETURN '{}'::jsonb;
END
$$;

-- Functions are executable by PUBLIC by default: take that away, then allow only Supabase Auth.
REVOKE ALL ON FUNCTION c3_auth.before_user_created(jsonb) FROM PUBLIC;
DO $$
DECLARE r text;
BEGIN
  FOREACH r IN ARRAY ARRAY['anon', 'authenticated', 'service_role'] LOOP
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
      EXECUTE format('REVOKE ALL ON SCHEMA c3_auth FROM %I', r);
      EXECUTE format('REVOKE ALL ON FUNCTION c3_auth.before_user_created(jsonb) FROM %I', r);
    END IF;
  END LOOP;
END $$;
GRANT USAGE ON SCHEMA c3_auth TO supabase_auth_admin;
GRANT EXECUTE ON FUNCTION c3_auth.before_user_created(jsonb) TO supabase_auth_admin;

-- Check (read-only): who can execute it, and two sample decisions. Expected: only supabase_auth_admin (and the owner);
-- allowed = {}, refused = {"error": {...}}.
SELECT r.rolname, has_function_privilege(r.oid, 'c3_auth.before_user_created(jsonb)', 'EXECUTE') AS can_execute
FROM pg_roles r WHERE r.rolname IN ('anon', 'authenticated', 'service_role', 'supabase_auth_admin', 'authenticator')
ORDER BY 1;
SELECT c3_auth.before_user_created('{"user":{"email":"Pat@PowerEdison.com"}}') AS allowed,
       c3_auth.before_user_created('{"user":{"email":"pat@mail.poweredison.com"}}') AS refused;

-- Undo (only if approved): first switch the hook off in Authentication -> Hooks, then
--   DROP FUNCTION c3_auth.before_user_created(jsonb); DROP SCHEMA c3_auth;
