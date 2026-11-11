-- C3 Site Finder: READ-ONLY inventory of Supabase Auth accounts before changing Auth:AccessRule.
-- Changes nothing, writes nothing. Run in the SQL Editor as postgres.
--
-- Paste two Render values, as they are, between the quotes on lines A and B below (each once; commas, spaces,
-- semicolons or new lines between ids are all fine). Leave a value empty ('') if the variable is not set.
--   A: Auth__AllowedUserIds          B: Auth__DomainExceptionUserIds (approved administrator exceptions)
-- Check "ids_on_allowlist" / "ids_as_exception" in the result: they must equal the number of ids on Render.
--
-- "verdict" says what each account gets:
--   step 1 = Auth__AccessRule=EmailDomainAndUserIds   (on the allowlist AND (company address OR exception))
--   step 3 = Auth__AccessRule=EmailDomain             (company address OR exception; allowlist removed)
-- Every rule also needs a confirmed, unbanned, non-anonymous account.
WITH params AS (
  SELECT '' ::text AS allowed_ids,     -- A: paste Auth__AllowedUserIds here
         '' ::text AS exception_ids    -- B: paste Auth__DomainExceptionUserIds here
),
allowlist AS (
  SELECT DISTINCT x::uuid AS id FROM params, regexp_split_to_table(params.allowed_ids, '[,;\s]+') AS x
  WHERE x ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
),
exceptions AS (
  SELECT DISTINCT x::uuid AS id FROM params, regexp_split_to_table(params.exception_ids, '[,;\s]+') AS x
  WHERE x ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
),
listed AS (SELECT id FROM allowlist UNION SELECT id FROM exceptions),
u AS (
  SELECT u.id, u.email, u.email_confirmed_at, u.banned_until, u.last_sign_in_at, u.created_at,
         u.email_confirmed_at IS NOT NULL AND NOT coalesce(u.banned_until > now(), false)
           AND NOT coalesce(u.is_anonymous, false) AS usable,
         u.email IS NOT NULL AND u.email ~ '^[!-~]+$'
           AND length(u.email) - length(replace(u.email, '@', '')) = 1 AND strpos(u.email, '@') > 1
           AND lower(split_part(u.email, '@', 2)) IN ('poweredison.com', 'ev-edison.com') AS company_address
  FROM auth.users u
),
rows AS (
  SELECT coalesce(u.email, '(no account)') AS email, coalesce(u.id, l.id) AS user_id,
         coalesce(u.id, l.id) IN (SELECT id FROM allowlist) AS on_allowlist,
         coalesce(u.id, l.id) IN (SELECT id FROM exceptions) AS exception,
         u.id IS NOT NULL AS has_account, coalesce(u.usable, false) AS usable,
         coalesce(u.company_address, false) AS company_address,
         u.email_confirmed_at, u.banned_until, u.last_sign_in_at
  FROM u FULL JOIN listed l ON l.id = u.id
)
SELECT email, user_id, on_allowlist, exception, company_address, usable, email_confirmed_at, banned_until, last_sign_in_at,
       CASE
         WHEN NOT has_account THEN 'STALE ID: no such account, remove it from Render'
         WHEN (on_allowlist OR exception) AND NOT usable THEN 'NO ACCESS (unconfirmed, banned or anonymous)'
         WHEN on_allowlist AND (company_address OR exception) THEN 'keeps access (step 1 and 3)'
         WHEN on_allowlist THEN 'LOSES ACCESS at step 1 (personal address, not an exception)'
         WHEN exception THEN 'exception NOT on allowlist: no access at step 1, access at step 3'
         WHEN company_address AND usable THEN 'GAINS ACCESS at step 3 (company address)'
         ELSE 'no access (before and after)'
       END AS verdict,
       (SELECT count(*) FROM allowlist) AS ids_on_allowlist,
       (SELECT count(*) FROM exceptions) AS ids_as_exception
FROM rows
ORDER BY verdict, email;
