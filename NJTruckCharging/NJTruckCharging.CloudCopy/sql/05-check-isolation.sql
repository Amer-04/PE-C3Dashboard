-- C3 Site Finder: READ-ONLY isolation check. Run in the SQL Editor as postgres after step 4. Every row must say OK.
-- With shared methodology editing installed (09/10), it also proves the writer role can change ONLY methodology data.
WITH c3t AS (SELECT c.oid, c.relname, c.relrowsecurity FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'c3' AND c.relkind = 'r'),
     -- Application schemas (public, private, migration_import, ...): the C3 roles must have NO access at all.
     othert AS (SELECT c.oid, n.oid AS nspoid, n.nspname || '.' || c.relname AS name
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname NOT IN ('c3', 'pg_catalog', 'information_schema') AND n.nspname !~ '^pg_'
                  AND pg_get_userbyid(n.nspowner) NOT IN ('supabase_admin', 'supabase_auth_admin', 'supabase_storage_admin')
                  AND c.relkind IN ('r', 'p', 'v', 'm')),
     -- Supabase-managed schemas: objects Supabase grants to every role (e.g. pg_net's net.http_request_queue).
     managed AS (SELECT n.nspname || '.' || c.relname AS name, c.oid FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                 WHERE pg_get_userbyid(n.nspowner) IN ('supabase_admin', 'supabase_auth_admin', 'supabase_storage_admin')
                   AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname !~ '^pg_'
                   AND c.relkind IN ('r', 'p', 'v', 'm')),
     api AS (SELECT rolname FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'service_role', 'authenticator')),
     -- The optional methodology writer (absent = editing not installed; its checks then report that and pass).
     w AS (SELECT oid FROM pg_roles WHERE rolname = 'c3_methodology_writer'),
     mtables AS (SELECT oid, relname FROM c3t WHERE relname IN
                 ('MethodologyVersions', 'SiteVersionScores', 'MethodologyState', 'MethodologyActivations', 'MethodologyChangeLog', 'CandidateSites')),
     c3roles AS (SELECT rolname AS r FROM pg_roles WHERE rolname IN ('c3_migrator', 'c3_runtime_readonly', 'c3_methodology_writer')),
     checks AS (
  SELECT 'schema c3 has the 19 C3 tables' AS check_name, (SELECT count(*) FROM c3t) = 19 AS ok,
         (SELECT count(*) FROM c3t)::text AS detail
  UNION ALL SELECT 'row-level security on every c3 table', bool_and(relrowsecurity),
         count(*) FILTER (WHERE NOT relrowsecurity)::text || ' without' FROM c3t
  UNION ALL SELECT 'runtime role can SELECT every c3 table',
         bool_and(has_table_privilege('c3_runtime_readonly', oid, 'SELECT')), '' FROM c3t
  UNION ALL SELECT 'runtime role cannot write any c3 table',
         NOT bool_or(has_table_privilege('c3_runtime_readonly', oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')), '' FROM c3t
  UNION ALL SELECT 'runtime role cannot create objects in c3', NOT has_schema_privilege('c3_runtime_readonly', 'c3', 'CREATE'), ''
  UNION ALL SELECT 'Supabase API roles cannot use schema c3',
         NOT coalesce(bool_or(has_schema_privilege(rolname, 'c3', 'USAGE')), false),
         coalesce(string_agg(rolname, ',') FILTER (WHERE has_schema_privilege(rolname, 'c3', 'USAGE')), '') FROM api
  UNION ALL SELECT 'Supabase API roles have no privileges on c3 tables',
         NOT coalesce(bool_or(has_table_privilege(a.rolname, t.oid, 'SELECT,INSERT,UPDATE,DELETE')), false), ''
         FROM api a CROSS JOIN c3t t
  UNION ALL SELECT 'KNOWN EXCEPTION (Supabase-managed, granted to every role; not C3-specific)',
         true, coalesce(string_agg(DISTINCT m.name, ', '), 'none')
         FROM managed m WHERE has_table_privilege('c3_runtime_readonly', m.oid, 'SELECT,INSERT,UPDATE,DELETE')
  -- Effective access needs BOTH a table privilege AND USAGE on the table's schema. A table privilege inside a schema
  -- the role cannot use (e.g. a view granted to PUBLIC in a schema open only to Supabase's API roles) is unreachable:
  -- listed in the detail for transparency, but not a failure.
  UNION ALL SELECT 'c3 roles cannot reach any application schema''s tables (table privilege + schema USAGE)',
         NOT coalesce(bool_or(has_schema_privilege(r, o.nspoid, 'USAGE')
                              AND has_table_privilege(r, o.oid, 'SELECT,INSERT,UPDATE,DELETE')), false),
         coalesce('REACHABLE: ' || string_agg(DISTINCT r || ' -> ' || o.name, ', ')
                  FILTER (WHERE has_schema_privilege(r, o.nspoid, 'USAGE')
                                AND has_table_privilege(r, o.oid, 'SELECT,INSERT,UPDATE,DELETE')) || '; ', '')
         || coalesce('granted but unreachable (no schema USAGE): ' || string_agg(DISTINCT o.name, ', ')
                  FILTER (WHERE NOT has_schema_privilege(r, o.nspoid, 'USAGE')
                                AND has_table_privilege(r, o.oid, 'SELECT,INSERT,UPDATE,DELETE')), '')
         FROM othert o CROSS JOIN c3roles
  UNION ALL SELECT 'c3 roles cannot create in schema public',
         NOT (has_schema_privilege('c3_migrator', 'public', 'CREATE') OR has_schema_privilege('c3_runtime_readonly', 'public', 'CREATE')), ''
  UNION ALL SELECT 'schema c3 owned by the administrator (not by a c3 role)',
         pg_get_userbyid(n.nspowner) NOT IN ('c3_migrator', 'c3_runtime_readonly'), pg_get_userbyid(n.nspowner)
         FROM pg_namespace n WHERE n.nspname = 'c3'
  UNION ALL SELECT 'c3 roles cannot create schemas (no CREATE on the database)',
         NOT (has_database_privilege('c3_migrator', current_database(), 'CREATE')
              OR has_database_privilege('c3_runtime_readonly', current_database(), 'CREATE')), ''
  UNION ALL SELECT 'c3_migrator locked (no login, no CREATE; USAGE only while the methodology writer exists)',
         NOT r.rolcanlogin AND NOT has_schema_privilege('c3_migrator', 'c3', 'CREATE')
         AND (NOT has_schema_privilege('c3_migrator', 'c3', 'USAGE') OR EXISTS (SELECT 1 FROM w)),
         CASE WHEN has_schema_privilege('c3_migrator', 'c3', 'USAGE') THEN 'USAGE kept for foreign-key checks of methodology writes' ELSE '' END
         FROM pg_roles r WHERE r.rolname = 'c3_migrator'
  UNION ALL SELECT 'runtime role uses read-only transactions',
         coalesce(array_to_string(r.rolconfig, ',') LIKE '%default_transaction_read_only=on%', false), array_to_string(r.rolconfig, ', ')
         FROM pg_roles r WHERE r.rolname = 'c3_runtime_readonly'
  UNION ALL SELECT 'c3 roles: no superuser / createrole / createdb / bypassrls',
         NOT bool_or(rolsuper OR rolcreaterole OR rolcreatedb OR rolbypassrls), ''
         FROM pg_roles WHERE rolname IN ('c3_migrator', 'c3_runtime_readonly', 'c3_methodology_writer')
  -- ---- methodology writer (only when installed) ----
  UNION ALL SELECT 'methodology writer installed', true,
         CASE WHEN EXISTS (SELECT 1 FROM w) THEN 'yes' ELSE 'no (shared methodology editing not installed)' END
  UNION ALL SELECT 'writer has NO privilege on any non-methodology c3 table',
         NOT coalesce(bool_or(has_table_privilege(w.oid, t.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')), false),
         coalesce(string_agg(t.relname, ', ') FILTER (WHERE has_table_privilege(w.oid, t.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')), '')
         FROM w CROSS JOIN c3t t WHERE t.oid NOT IN (SELECT oid FROM mtables)
  UNION ALL SELECT 'writer cannot TRUNCATE or alter methodology tables (no TRUNCATE/REFERENCES/TRIGGER)',
         NOT coalesce(bool_or(has_table_privilege(w.oid, t.oid, 'TRUNCATE,REFERENCES,TRIGGER')), false), ''
         FROM w CROSS JOIN mtables t
  UNION ALL SELECT 'writer: CandidateSites only score columns updatable, no insert/delete',
         NOT has_table_privilege(w.oid, 'c3."CandidateSites"', 'INSERT,DELETE')
         AND has_column_privilege(w.oid, 'c3."CandidateSites"', 'Score', 'UPDATE')
         AND NOT has_column_privilege(w.oid, 'c3."CandidateSites"', 'SiteAddress', 'UPDATE')
         AND NOT has_column_privilege(w.oid, 'c3."CandidateSites"', 'OpenAcresEst', 'UPDATE')
         AND NOT has_column_privilege(w.oid, 'c3."CandidateSites"', 'SiteId', 'UPDATE'), ''
         FROM w
  UNION ALL SELECT 'writer: history and audit are append-only (no UPDATE/DELETE)',
         NOT has_table_privilege(w.oid, 'c3."MethodologyActivations"', 'UPDATE,DELETE')
         AND NOT has_table_privilege(w.oid, 'c3."MethodologyChangeLog"', 'UPDATE,DELETE'), ''
         FROM w
  UNION ALL SELECT 'writer: cannot change the baseline flag or creation audit of versions',
         NOT has_column_privilege(w.oid, 'c3."MethodologyVersions"', 'IsBaseline', 'UPDATE')
         AND NOT has_column_privilege(w.oid, 'c3."MethodologyVersions"', 'CreatedByUserId', 'UPDATE')
         AND NOT has_column_privilege(w.oid, 'c3."MethodologyState"', 'DataVersion', 'UPDATE'), ''
         FROM w
  UNION ALL SELECT 'writer: immutability trigger on MethodologyVersions',
         EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'c3_methodology_version_guard' AND NOT tgisinternal AND tgenabled <> 'D'), ''
         FROM w
  UNION ALL SELECT 'writer cannot create objects in c3 and is not read-only-bypassing',
         NOT has_schema_privilege(w.oid, 'c3', 'CREATE') AND NOT has_database_privilege(w.oid, current_database(), 'CREATE'), ''
         FROM w)
SELECT CASE WHEN ok THEN 'OK' ELSE 'FAIL' END AS result, check_name, detail FROM checks;
