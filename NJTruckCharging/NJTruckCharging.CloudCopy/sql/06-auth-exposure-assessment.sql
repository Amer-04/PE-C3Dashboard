-- C3 Site Finder: READ-ONLY assessment of what ANY signed-in ("authenticated") or anonymous ("anon") user of this
-- Supabase project can reach through the Data API (REST/GraphQL), Storage and Realtime. Changes nothing, writes nothing.
-- Run in the SQL Editor as postgres. Results: one row per finding; "effective" = grant AND (no RLS OR a policy allows it).

WITH exposed AS (
  -- Schemas the Data API serves (Dashboard -> Project Settings -> Data API). Falls back to "public" when not set on the role.
  SELECT DISTINCT trim(s) AS nspname
  FROM pg_roles r, unnest(coalesce(r.rolconfig, ARRAY[]::text[])) cfg,
       unnest(string_to_array(CASE WHEN cfg LIKE 'pgrst.db_schemas=%' THEN substr(cfg, 18) END, ',')) s
  WHERE r.rolname = 'authenticator'
  UNION SELECT 'public'
),
objs AS (
  SELECT c.oid, n.nspname, c.relname,
         CASE c.relkind WHEN 'r' THEN 'table' WHEN 'p' THEN 'table' WHEN 'v' THEN 'view' WHEN 'm' THEN 'matview' WHEN 'f' THEN 'foreign' END AS kind,
         c.relrowsecurity AS rls, c.relforcerowsecurity AS rls_forced, pg_get_userbyid(c.relowner) AS owner,
         coalesce((SELECT bool_or(o = 'security_invoker=true' OR o = 'security_invoker=on') FROM unnest(c.reloptions) o), false) AS security_invoker,
         -- For views: the tables they read from (their row-level security decides what a caller sees).
         (SELECT string_agg(DISTINCT bn.nspname || '.' || bc.relname ||
                   CASE WHEN bc.relkind IN ('r', 'p') AND NOT bc.relrowsecurity THEN ' [NO RLS]'
                        WHEN bc.relkind IN ('r', 'p') THEN ' [rls]' ELSE ' [' || bc.relkind::text || ']' END, ', ')
          FROM pg_rewrite rw JOIN pg_depend d ON d.objid = rw.oid AND d.classid = 'pg_rewrite'::regclass
          JOIN pg_class bc ON bc.oid = d.refobjid AND bc.oid <> c.oid JOIN pg_namespace bn ON bn.oid = bc.relnamespace
          WHERE rw.ev_class = c.oid) AS base_tables
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname IN (SELECT nspname FROM exposed) AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
),
roles AS (SELECT unnest(ARRAY['anon', 'authenticated']) AS r),
grants AS (
  SELECT o.*, ro.r AS api_role,
         concat_ws('/', CASE WHEN has_table_privilege(ro.r, o.oid, 'SELECT') THEN 'S' END,
                        CASE WHEN has_table_privilege(ro.r, o.oid, 'INSERT') THEN 'I' END,
                        CASE WHEN has_table_privilege(ro.r, o.oid, 'UPDATE') THEN 'U' END,
                        CASE WHEN has_table_privilege(ro.r, o.oid, 'DELETE') THEN 'D' END,
                        CASE WHEN has_table_privilege(ro.r, o.oid, 'TRUNCATE') THEN 'TRUNCATE' END) AS granted
  FROM objs o CROSS JOIN roles ro
),
pol AS (
  SELECT p.schemaname, p.tablename, p.policyname, p.permissive, p.cmd, p.roles, p.qual, p.with_check,
         -- The expression that actually decides: INSERT has only WITH CHECK; SELECT/DELETE only USING;
         -- UPDATE/ALL both (a missing WITH CHECK reuses USING).
         CASE WHEN p.cmd = 'INSERT' THEN coalesce(p.with_check, 'true')
              WHEN p.cmd IN ('SELECT', 'DELETE') THEN coalesce(p.qual, 'true')
              ELSE coalesce(p.qual, 'true') || ' / ' || coalesce(p.with_check, p.qual, 'true') END AS deciding
  FROM pg_policies p WHERE p.schemaname IN (SELECT nspname FROM exposed)
),
-- Relations in the other application's schemas (everything except system, Supabase-managed and c3 schemas).
appobjs AS (
  SELECT c.oid, n.oid AS nspoid, n.nspname, c.relname
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'c3') AND n.nspname !~ '^pg_'
    AND pg_get_userbyid(n.nspowner) NOT IN ('supabase_admin', 'supabase_auth_admin', 'supabase_storage_admin', 'pgbouncer')
    AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
),
c3_roles AS (
  SELECT name, EXISTS (SELECT 1 FROM pg_roles WHERE rolname = name) AS present
  FROM unnest(ARRAY['c3_migrator', 'c3_runtime_readonly']) AS name
),
c3_offenders AS (   -- evaluated only for roles that exist (has_*_privilege fails for unknown roles)
  SELECT r.rolname, string_agg(a.nspname || '.' || a.relname, ', ' ORDER BY a.nspname, a.relname) AS v
  FROM pg_roles r CROSS JOIN appobjs a
  WHERE r.rolname IN ('c3_migrator', 'c3_runtime_readonly')
    AND has_schema_privilege(r.rolname, a.nspoid, 'USAGE')
    AND has_table_privilege(r.rolname, a.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
  GROUP BY r.rolname
),
public_prediction AS (   -- a newly created role starts with exactly the PUBLIC grants
  SELECT string_agg(a.nspname || '.' || a.relname, ', ') AS v
  FROM appobjs a
  WHERE has_schema_privilege('public', a.nspoid, 'USAGE') AND has_table_privilege('public', a.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
)
-- 1. Objects and effective access per API role
SELECT '1 object' AS section, g.nspname || '.' || g.relname AS object, g.kind, g.api_role,
       g.granted AS granted_privileges,
       CASE WHEN g.granted = '' THEN 'no access'
            WHEN g.kind = 'view' AND NOT g.security_invoker THEN 'VIEW RUNS AS OWNER ' || g.owner || ' (bypasses caller RLS) -> effective: ' || g.granted
            WHEN g.kind = 'view' THEN 'view (security_invoker): RLS of its base tables applies'
            WHEN g.kind IN ('table') AND NOT g.rls THEN 'NO RLS -> effective: ' || g.granted || ' on ALL rows'
            WHEN g.kind = 'matview' THEN 'materialized view (no RLS) -> effective: ' || g.granted
            ELSE 'RLS on -> see policies' END AS assessment,
       CASE WHEN g.kind = 'view' THEN 'reads: ' || coalesce(g.base_tables, '?') WHEN g.rls_forced THEN 'forced' ELSE '' END AS detail
FROM grants g
UNION ALL
-- 2. Every policy that applies to anon/authenticated/public, with its exact expressions
SELECT '2 policy', p.schemaname || '.' || p.tablename, p.cmd, array_to_string(p.roles, '+'), p.policyname || ' (' || p.permissive || ')',
       'USING: ' || coalesce(p.qual, '(none)'),
       'WITH CHECK: ' || coalesce(p.with_check, '(none)') ||
       CASE WHEN p.deciding ~ '(^|/ )\(?true\)?( /|$)'
            THEN '   <-- ALLOWS EVERY USER IN THESE ROLES'
            WHEN p.deciding ~ 'auth\.(uid|jwt|email|role)|current_user|request\.jwt'
            THEN '   (tied to the signed-in user)'
            WHEN p.deciding ~ '[a-z_]+\('
            THEN '   <-- decided by function(s) ' || (SELECT string_agg(DISTINCT m[1], ', ') FROM regexp_matches(p.deciding, '([a-z_][a-z0-9_]*)\(', 'g') AS m) || ' (see section 9)'
            ELSE '   <-- NOT TIED TO THE SIGNED-IN USER (review)' END
FROM pol p
WHERE p.roles && ARRAY['anon', 'authenticated', 'public']::name[]
UNION ALL
-- 3. Functions callable through the API (RPC) by anon/authenticated
SELECT '3 function', n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')',
       CASE WHEN p.prosecdef THEN 'SECURITY DEFINER' ELSE 'invoker' END, ro.r,
       CASE p.provolatile WHEN 'v' THEN 'volatile (may write)' WHEN 's' THEN 'stable' ELSE 'immutable' END,
       'owner ' || pg_get_userbyid(p.proowner), ''
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace CROSS JOIN roles ro
WHERE n.nspname IN (SELECT nspname FROM exposed) AND p.prokind IN ('f', 'p')
  AND has_function_privilege(ro.r, p.oid, 'EXECUTE')
  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')   -- skip extension functions
UNION ALL
-- 4. Data API configuration
SELECT '4 data api', 'authenticator', 'role config', '', array_to_string(rolconfig, ' '), '', ''
FROM pg_roles WHERE rolname = 'authenticator'
UNION ALL
SELECT '4 data api', 'pg_graphql', 'extension', '', CASE WHEN EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_graphql')
       THEN 'installed: the same tables are reachable through /graphql/v1 with the same grants/RLS' ELSE 'not installed' END, '', ''
UNION ALL
-- 5. Storage buckets and their policies
SELECT '5 storage', 'bucket ' || b.name, CASE WHEN b.public THEN 'PUBLIC bucket' ELSE 'private bucket' END, '', '', '', ''
FROM storage.buckets b
UNION ALL
SELECT '5 storage', 'storage.objects policy ' || p.policyname, p.cmd, array_to_string(p.roles, '+'), '',
       'USING: ' || coalesce(p.qual, '(none)'), 'WITH CHECK: ' || coalesce(p.with_check, '(none)')
FROM pg_policies p WHERE p.schemaname = 'storage' AND p.tablename = 'objects'
UNION ALL
-- 6. Realtime: tables broadcast to subscribers (Realtime applies RLS for postgres_changes)
SELECT '6 realtime', pt.schemaname || '.' || pt.tablename, 'in publication ' || pt.pubname, '', '', '', ''
FROM pg_publication_tables pt WHERE pt.pubname LIKE 'supabase_realtime%'
UNION ALL
-- 7. Proposed C3 roles vs. every application schema: always one row per role, PASS / FAIL / NOT YET TESTABLE.
--    Before the roles exist, the PUBLIC grants predict the result (a new role starts with only those).
SELECT '7 c3 roles', 'all application schemas (' || coalesce((SELECT string_agg(DISTINCT nspname, ', ') FROM appobjs), 'none') || ')',
       'summary', r.name,
       CASE WHEN NOT r.present THEN 'NOT YET TESTABLE'
            WHEN o.v IS NULL THEN 'PASS' ELSE 'FAIL' END,
       CASE WHEN NOT r.present
            THEN 'role not created yet - PREDICTED ' || CASE WHEN pp.v IS NULL THEN 'PASS (nothing in these schemas is granted to every role)'
                                                            ELSE 'FAIL: granted to every role: ' || pp.v END
            WHEN o.v IS NULL THEN 'no privileges on any application table or view'
            ELSE 'has privileges on: ' || o.v END,
       'not a member of anon/authenticated, so their policies never apply'
FROM c3_roles r LEFT JOIN c3_offenders o ON o.rolname = r.name CROSS JOIN public_prediction pp
UNION ALL
-- 8. Objects in ANY non-system schema (public, private, migration_import, ...) granted to PUBLIC: every role,
--    including the future C3 roles, would inherit these. Supabase-managed schemas are listed separately for context.
SELECT '8 granted to PUBLIC', n.nspname || '.' || c.relname,
       CASE c.relkind WHEN 'v' THEN 'view' WHEN 'm' THEN 'matview' WHEN 'f' THEN 'foreign' ELSE 'table' END, 'PUBLIC',
       concat_ws('/', CASE WHEN has_table_privilege('public', c.oid, 'SELECT') THEN 'S' END,
                      CASE WHEN has_table_privilege('public', c.oid, 'INSERT') THEN 'I' END,
                      CASE WHEN has_table_privilege('public', c.oid, 'UPDATE') THEN 'U' END,
                      CASE WHEN has_table_privilege('public', c.oid, 'DELETE') THEN 'D' END),
       CASE WHEN has_schema_privilege('public', n.oid, 'USAGE') THEN 'schema usable by every role' ELSE 'schema NOT usable by every role (not reachable)' END,
       CASE WHEN pg_get_userbyid(n.nspowner) IN ('supabase_admin', 'supabase_auth_admin', 'supabase_storage_admin') THEN 'Supabase-managed schema' ELSE '' END
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_' AND n.nspname <> 'c3'
  AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
  AND has_table_privilege('public', c.oid, 'SELECT,INSERT,UPDATE,DELETE')
UNION ALL
SELECT '8 granted to PUBLIC', n.nspname || '.' || p.proname, 'SECURITY DEFINER function', 'PUBLIC', 'EXECUTE', 'owner ' || pg_get_userbyid(p.proowner), ''
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_' AND p.prosecdef
  AND has_function_privilege('public', p.oid, 'EXECUTE')
  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
UNION ALL
-- 9. Source of every non-extension function a signed-in user can call (includes the helpers used by the policies,
--    e.g. membership/admin checks). Read the body to see WHO each one lets in.
SELECT '9 function source', n.nspname || '.' || p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')',
       CASE WHEN p.prosecdef THEN 'SECURITY DEFINER' ELSE 'invoker' END,
       concat_ws('+', CASE WHEN has_function_privilege('anon', p.oid, 'EXECUTE') THEN 'anon' END,
                      CASE WHEN has_function_privilege('authenticated', p.oid, 'EXECUTE') THEN 'authenticated' END,
                      CASE WHEN has_function_privilege('public', p.oid, 'EXECUTE') THEN 'PUBLIC' END),
       CASE p.provolatile WHEN 'v' THEN 'volatile (may write)' WHEN 's' THEN 'stable' ELSE 'immutable' END,
       regexp_replace(pg_get_functiondef(p.oid), '\s+', ' ', 'g'),
       coalesce('config: ' || array_to_string(p.proconfig, ' '), '')
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname IN (SELECT nspname FROM exposed) AND p.prokind IN ('f', 'p')
  AND (has_function_privilege('authenticated', p.oid, 'EXECUTE') OR has_function_privilege('anon', p.oid, 'EXECUTE'))
  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
ORDER BY 1, 2, 4;
