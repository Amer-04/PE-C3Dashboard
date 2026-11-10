-- C3 Site Finder: step 3 of 4, table permissions inside schema c3. Run as c3_migrator (the owner of the c3 tables)
-- after the data copy; the CloudCopy "secure" command runs this file. Schema-level permissions were set by the
-- administrator in step 1 (schema c3 is owned by the administrator, not by c3_migrator).
-- c3_runtime_readonly: SELECT only. Supabase API roles and PUBLIC: nothing. Row-level security on every table.
DO $$
DECLARE r record; t record;
BEGIN
  FOR r IN SELECT rolname FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'service_role', 'authenticator') LOOP
    EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA c3 FROM %I', r.rolname);
    EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA c3 FROM %I', r.rolname);
    EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA c3 FROM %I', r.rolname);
  END LOOP;
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'c3' LOOP
    EXECUTE format('REVOKE ALL ON c3.%I FROM PUBLIC', t.tablename);
    EXECUTE format('REVOKE ALL ON c3.%I FROM c3_runtime_readonly', t.tablename);
    EXECUTE format('GRANT SELECT ON c3.%I TO c3_runtime_readonly', t.tablename);
    EXECUTE format('ALTER TABLE c3.%I ENABLE ROW LEVEL SECURITY', t.tablename);
    EXECUTE format('DROP POLICY IF EXISTS c3_runtime_read ON c3.%I', t.tablename);
    EXECUTE format('CREATE POLICY c3_runtime_read ON c3.%I FOR SELECT TO c3_runtime_readonly USING (true)', t.tablename);
  END LOOP;
END $$;
REVOKE ALL ON ALL SEQUENCES IN SCHEMA c3 FROM PUBLIC;
