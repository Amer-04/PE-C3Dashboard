-- C3 Site Finder: ROLLBACK of shared methodology editing. Run as c3_migrator with
-- "NJTruckCharging.CloudCopy methodology-editing-rollback" (re-open c3_migrator first, exactly as for step B).
-- Idempotent.
--
-- Removes every permission and policy of c3_methodology_writer and the immutability trigger. KEEPS ALL DATA: the
-- methodology versions, the active scores, the activation history and the audit columns / MethodologyChangeLog stay
-- (the API ignores nothing it needs; older API builds simply never read the extra columns). Then drop the role itself
-- in the SQL Editor as postgres: 09-methodology-writer-role-rollback.sql, then 04-lockdown.sql.
--
-- Removing the audit schema as well is a separate, deliberate step that DELETES THE AUDIT HISTORY; it is at the end of
-- this file, commented out. Export MethodologyChangeLog first if you ever run it.
DO $$
DECLARE t record;
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'c3_methodology_writer') THEN
    FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'c3' LOOP
      EXECUTE format('REVOKE ALL ON c3.%I FROM c3_methodology_writer', t.tablename);
    END LOOP;
    REVOKE ALL ON ALL SEQUENCES IN SCHEMA c3 FROM c3_methodology_writer;
  END IF;
  FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'c3' LOOP
    EXECUTE format('DROP POLICY IF EXISTS c3_methodology_writer_select ON c3.%I', t.tablename);
    EXECUTE format('DROP POLICY IF EXISTS c3_methodology_writer_insert ON c3.%I', t.tablename);
    EXECUTE format('DROP POLICY IF EXISTS c3_methodology_writer_update ON c3.%I', t.tablename);
    EXECUTE format('DROP POLICY IF EXISTS c3_methodology_writer_delete ON c3.%I', t.tablename);
  END LOOP;
END $$;
DROP TRIGGER IF EXISTS c3_methodology_version_guard ON c3."MethodologyVersions";
DROP FUNCTION IF EXISTS c3.c3_methodology_version_guard();

-- ---- OPTIONAL, DESTROYS THE AUDIT HISTORY (not C3 data). Only with an export of MethodologyChangeLog. ------------
-- DROP TABLE IF EXISTS c3."MethodologyChangeLog";
-- ALTER TABLE c3."MethodologyActivations" DROP COLUMN IF EXISTS "ActorUserId", DROP COLUMN IF EXISTS "ActorEmail";
-- ALTER TABLE c3."MethodologyVersions" DROP COLUMN IF EXISTS "Revision", DROP COLUMN IF EXISTS "CreatedByUserId",
--     DROP COLUMN IF EXISTS "CreatedByEmail", DROP COLUMN IF EXISTS "UpdatedByUserId", DROP COLUMN IF EXISTS "UpdatedByEmail",
--     DROP COLUMN IF EXISTS "PublishedByUserId", DROP COLUMN IF EXISTS "PublishedByEmail";
