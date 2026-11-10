-- C3 Site Finder: READ-ONLY inventory of a Supabase project (run in the SQL Editor as postgres).
-- Changes nothing. Returns one result table; paste it back for review. No passwords or row data are shown.
WITH
  db AS (SELECT version() AS pg_version, pg_size_pretty(pg_database_size(current_database())) AS db_size,
                current_setting('max_connections') AS max_connections,
                (SELECT count(*) FROM pg_stat_activity) AS connections_now),
  schemas AS (SELECT string_agg(n.nspname || ' (owner ' || pg_get_userbyid(n.nspowner) || ', ' ||
                     pg_size_pretty(coalesce((SELECT sum(pg_total_relation_size(c.oid)) FROM pg_class c
                                              WHERE c.relnamespace = n.oid AND c.relkind IN ('r', 'm')), 0)) || ')',
                     '; ' ORDER BY n.nspname) AS v
              FROM pg_namespace n WHERE n.nspname !~ '^(pg_|information_schema)'),
  roles AS (SELECT string_agg(rolname || CASE WHEN rolcanlogin THEN ' [login]' ELSE '' END ||
                     CASE WHEN rolsuper THEN ' [super]' ELSE '' END ||
                     CASE WHEN rolbypassrls THEN ' [bypassrls]' ELSE '' END ||
                     CASE WHEN rolcreaterole THEN ' [createrole]' ELSE '' END, ', ' ORDER BY rolname) AS v
            FROM pg_roles WHERE rolname !~ '^pg_'),
  me AS (SELECT current_user || CASE WHEN r.rolsuper THEN ' (superuser)' ELSE '' END ||
                CASE WHEN r.rolcreaterole THEN ' (createrole)' ELSE '' END ||
                CASE WHEN has_database_privilege(current_database(), 'CREATE') THEN ' (can create schemas)' ELSE '' END AS v
         FROM pg_roles r WHERE r.rolname = current_user),
  c3_collisions AS (SELECT coalesce(string_agg(x, ', '), 'none') AS v FROM (
      SELECT 'schema c3' AS x FROM pg_namespace WHERE nspname = 'c3'
      UNION ALL SELECT 'role ' || rolname FROM pg_roles WHERE rolname LIKE 'c3\_%') s),
  public_tables AS (SELECT count(*) AS n,
      count(*) FILTER (WHERE NOT c.relrowsecurity) AS without_rls,
      count(*) FILTER (WHERE has_table_privilege('public', c.oid, 'SELECT')) AS readable_by_every_role
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm')),
  -- Cross-application exposure: what ANY signed-in user of the shared Auth could do through the Data API.
  authenticated_access AS (SELECT coalesce(string_agg(c.relname || ':' ||
        concat_ws('/', CASE WHEN has_table_privilege('authenticated', c.oid, 'SELECT') THEN 'S' END,
                       CASE WHEN has_table_privilege('authenticated', c.oid, 'INSERT') THEN 'I' END,
                       CASE WHEN has_table_privilege('authenticated', c.oid, 'UPDATE') THEN 'U' END,
                       CASE WHEN has_table_privilege('authenticated', c.oid, 'DELETE') THEN 'D' END) ||
        CASE WHEN c.relrowsecurity THEN ' rls' ELSE ' NO-RLS' END, ', ' ORDER BY c.relname), 'none') AS v
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm')
        AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated')
        AND has_table_privilege('authenticated', c.oid, 'SELECT,INSERT,UPDATE,DELETE')),
  open_policies AS (SELECT coalesce(string_agg(tablename || '.' || policyname || ' (' || cmd || ' to ' ||
        array_to_string(roles, '+') || ')', ', '), 'none') AS v
      FROM pg_policies WHERE schemaname = 'public'
        AND roles && ARRAY['authenticated', 'public']::name[]
        AND coalesce(qual, '') !~ 'auth\.(uid|jwt|email)' AND coalesce(with_check, '') !~ 'auth\.(uid|jwt|email)'),
  definer_functions AS (SELECT coalesce(string_agg(p.proname, ', '), 'none') AS v
      FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
      WHERE n.nspname = 'public' AND p.prosecdef AND has_function_privilege('public', p.oid, 'EXECUTE')),
  default_acls AS (SELECT coalesce(string_agg(pg_get_userbyid(d.defaclrole) || ' in ' || coalesce(n.nspname, 'ALL schemas') ||
        ' ' || d.defaclobjtype::text || ': ' || array_to_string(d.defaclacl::text[], ' '), '; '), 'none') AS v
      FROM pg_default_acl d LEFT JOIN pg_namespace n ON n.oid = d.defaclnamespace),
  event_triggers AS (SELECT coalesce(string_agg(evtname || ' on ' || evtevent || ' -> ' || evtfoid::regproc::text ||
        CASE WHEN evtenabled = 'D' THEN ' (disabled)' ELSE '' END, ', '), 'none') AS v FROM pg_event_trigger),
  api_settings AS (SELECT coalesce((SELECT array_to_string(rolconfig, ' ') FROM pg_roles WHERE rolname = 'authenticator'), 'n/a') AS v),
  auth_users AS (SELECT (SELECT count(*) FROM auth.users) AS users,
      (SELECT count(*) FROM auth.users WHERE is_anonymous) AS anonymous_users,
      (SELECT coalesce(string_agg(provider || '=' || n, ', '), 'none')
       FROM (SELECT provider, count(*) AS n FROM auth.identities GROUP BY provider) x) AS providers),
  extensions AS (SELECT string_agg(extname || ' ' || extversion, ', ' ORDER BY extname) AS v FROM pg_extension)
SELECT 'postgres version' AS item, (SELECT pg_version FROM db) AS value
UNION ALL SELECT 'database size (Free plan limit 500 MB)', (SELECT db_size FROM db)
UNION ALL SELECT 'connections now / max', (SELECT connections_now || ' / ' || max_connections FROM db)
UNION ALL SELECT 'running as', (SELECT v FROM me)
UNION ALL SELECT 'schemas (owner, size)', (SELECT v FROM schemas)
UNION ALL SELECT 'existing c3 schema / c3_* roles', (SELECT v FROM c3_collisions)
UNION ALL SELECT 'roles', (SELECT v FROM roles)
UNION ALL SELECT 'public tables / without RLS / readable by every role', (SELECT n || ' / ' || without_rls || ' / ' || readable_by_every_role FROM public_tables)
UNION ALL SELECT 'public objects ANY signed-in user can reach (S/I/U/D)', (SELECT v FROM authenticated_access)
UNION ALL SELECT 'public policies for authenticated/public not tied to the user', (SELECT v FROM open_policies)
UNION ALL SELECT 'SECURITY DEFINER functions in public executable by everyone', (SELECT v FROM definer_functions)
UNION ALL SELECT 'default privileges', (SELECT v FROM default_acls)
UNION ALL SELECT 'event triggers', (SELECT v FROM event_triggers)
UNION ALL SELECT 'Data API settings (authenticator role)', (SELECT v FROM api_settings)
UNION ALL SELECT 'auth users / anonymous / identities by provider', (SELECT users || ' / ' || anonymous_users || ' / ' || providers FROM auth_users)
UNION ALL SELECT 'extensions', (SELECT v FROM extensions);
