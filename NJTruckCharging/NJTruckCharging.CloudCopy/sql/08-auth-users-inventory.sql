-- C3 Site Finder: READ-ONLY inventory of existing Supabase Auth accounts before switching Auth:AccessRule.
-- Changes nothing, writes nothing. Run in the SQL Editor as postgres. Paste the current Render value of
-- Auth__AllowedUserIds (comma-separated UUIDs) between the quotes below.
--
-- Read the "after_switch" column: it says what each account gets under Auth:AccessRule=EmailDomain.
--   GAINS ACCESS  - not approved today, would be let in by the domain rule  -> make sure that is intended
--   LOSES ACCESS  - approved today, would be refused (personal address, unconfirmed, banned) -> tell the person first
--   keeps access / still refused - no change
WITH allowlist AS (
  SELECT trim(x)::uuid AS id
  FROM unnest(string_to_array(nullif('PASTE_AUTH__ALLOWEDUSERIDS_HERE', 'PASTE_AUTH__ALLOWEDUSERIDS_HERE'), ',')) x
  WHERE trim(x) <> ''
),
u AS (
  SELECT u.id, u.email, u.created_at, u.last_sign_in_at, u.email_confirmed_at, u.banned_until, u.is_anonymous,
         u.raw_app_meta_data ->> 'provider' AS provider,
         u.id IN (SELECT id FROM allowlist) AS approved_today,
         u.email IS NOT NULL AND u.email ~ '^[!-~]+$' AND length(u.email) - length(replace(u.email, '@', '')) = 1
           AND strpos(u.email, '@') > 1
           AND lower(split_part(u.email, '@', 2)) IN ('poweredison.com', 'ev-edison.com') AS company_address,
         u.email_confirmed_at IS NOT NULL AS confirmed,
         coalesce(u.banned_until > now(), false) AS banned
  FROM auth.users u
)
SELECT email, id, provider, created_at, last_sign_in_at, email_confirmed_at, banned_until, is_anonymous,
       approved_today, company_address, confirmed,
       CASE
         WHEN approved_today AND NOT (company_address AND confirmed AND NOT banned AND NOT is_anonymous) THEN 'LOSES ACCESS'
         WHEN NOT approved_today AND company_address AND confirmed AND NOT banned AND NOT is_anonymous THEN 'GAINS ACCESS'
         WHEN approved_today THEN 'keeps access'
         ELSE 'still refused'
       END AS after_switch
FROM u
ORDER BY after_switch, email;

-- Allowlisted ids that have no account at all (stale entries; harmless, remove them).
SELECT a.id AS allowlisted_id_without_account
FROM (SELECT trim(x)::uuid AS id
      FROM unnest(string_to_array(nullif('PASTE_AUTH__ALLOWEDUSERIDS_HERE', 'PASTE_AUTH__ALLOWEDUSERIDS_HERE'), ',')) x
      WHERE trim(x) <> '') a
WHERE NOT EXISTS (SELECT 1 FROM auth.users u WHERE u.id = a.id);
