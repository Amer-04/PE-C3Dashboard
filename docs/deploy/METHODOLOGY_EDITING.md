# Shared methodology editing (cloud)

Any signed-in C3 user, meaning a verified company employee or an approved administrator exception, can edit, review,
apply and restore the shared scoring methodology on the cloud deployment. The rest of the cloud deployment stays read-only.

## 1. How it is secured

| Layer | What it does |
|---|---|
| Sign-in (unchanged) | Every request needs a valid Supabase token (signature, issuer, audience, expiry). The account must be confirmed, not banned, not deleted, and its session not signed out. `Auth:AccessRule`, `Auth:BlockedUserIds` and `Auth:DomainExceptionUserIds` decide who is admitted. |
| Editor check (new) | Endpoints that change the methodology carry `RequireMethodologyEditor`. That check runs the access rule again on the server and requires the verified-e-mail claim, which only this server can issue. It refuses cross-site browser requests and records the Supabase user id and verified e-mail of the editor. CORS and the Angular UI are not the security control. |
| Read-only mode (kept) | `ReadOnlyDemo__Enabled=true` stays. When `Methodology__CloudEditing__Enabled=true`, only the routed endpoints that carry the editor marker are let through. The match uses endpoint metadata, not URL text. Saved sites, contacts, research, screening, feasibility, custom fields and admin import/rescore still answer `403 read_only`. |
| Database (new) | The API keeps the read-only login for everything. Methodology writes alone go through a second login, `c3_methodology_writer`, which can only do what the table below lists. |
| Integrity | Each draft save sends the revision it started from, and a stale save gets `409 draft_conflict` naming who changed it. Publishing requires typed confirmation, a current review, and the expected active version and rules hash. It runs in one transaction under a database lock, so a double-click or two people publishing applies the version once. Only one recalculation runs at a time; others get `409 scoring_busy`. A trigger makes published and baseline versions immutable for the writer. |
| Rate limits | Per user: 6 recalculations (Review Results / Apply / Restore) per 10 minutes and 120 edits per minute. Configurable; see §5. Excess requests get `429` with `Retry-After`. |
| Errors | A database failure returns `503 methodology_failed` with a plain message. The transaction is rolled back and the database error text goes only to the server log. |

### Permissions of `c3_methodology_writer`

| Table | Allowed | Not allowed |
|---|---|---|
| `MethodologyVersions` | read; insert **drafts**; update name/status/rules/summary/preview/audit columns; delete **drafts** | change the baseline flag or creation audit; change or delete published/baseline versions (trigger + row-level security) |
| `SiteVersionScores` | read, insert, delete (proposed scores) | truncate |
| `MethodologyState` | read; update the active/previous version pointer and time | change `DataVersion` |
| `MethodologyActivations`, `MethodologyChangeLog` | read, insert (append-only) | update, delete |
| `CandidateSites` | read; update **only** `Score`, `ComputedRank`, `TruckDemandPoints`, `ScoreVersionId`, `Breakdown_*` | insert, delete, any other column (address, acreage, owner data, …) |
| every other c3 table | **nothing** (not even read) | |
| schema c3 / database | use the schema | create anything; superuser, createrole, createdb and bypassrls are all off |

The role has a connection limit of 3, `statement_timeout` 120 s, `lock_timeout` 10 s and `idle_in_transaction_session_timeout` 60 s.
`sql/05-check-isolation.sql` proves these rules on the live project.

## 2. Files

| File | Run as | Purpose |
|---|---|---|
| `sql/09-methodology-writer-role.sql` | postgres (SQL Editor), printed by `CloudCopy writer-bootstrap-sql` | creates the writer login (SCRAM verifier only) and re-opens `c3_migrator` |
| `sql/10-methodology-editing.sql` | c3_migrator, by `CloudCopy methodology-editing` | **additive** schema change (audit columns + `MethodologyChangeLog`), writer grants, row-level security policies, immutability trigger, read-only SELECT on the new table. Idempotent. |
| `sql/04-lockdown.sql` (updated) | postgres | closes `c3_migrator` again. While the writer exists, `c3_migrator` keeps USAGE on schema c3, because PostgreSQL runs foreign-key checks as the table owner. It still cannot log in or create anything. |
| `sql/05-check-isolation.sql` (updated) | postgres | 23 checks, including the writer's exact permissions |
| `sql/10-methodology-editing-rollback.sql` | c3_migrator, by `CloudCopy methodology-editing-rollback` | removes grants, policies and the trigger. **Keeps all data**, including the audit trail. |
| `sql/09-methodology-writer-role-rollback.sql` | postgres | ends the writer's sessions and drops the login |
| EF migration `AddMethodologyEditingAudit` | local SQL Server (automatic for test/demo databases) | the same columns and table; `Down()` removes them |

## 3. Deployment (each step needs your approval)

**Order matters:** the new API reads the new columns. Run steps 1–3 before deploying the API in step 4.
Steps 1–3 do not change behaviour: the current API ignores the new columns, and nothing uses the writer yet.

1. **Writer login (Supabase SQL Editor, as postgres).** On the trusted machine, set a fresh random password of 24 or more
   printable characters, without quotes or `;`, and keep it in the password manager:
   `$env:C3_WRITER_PASSWORD = '<password>'`. Then run `dotnet run --project NJTruckCharging.CloudCopy -- writer-bootstrap-sql`.
   Paste the output into the SQL Editor and run it. It refuses to run twice.
2. **Schema and permissions (trusted machine, as c3_migrator).**
   `$env:C3_TARGET = 'Host=<pooler-host>;Port=5432;Database=postgres;Username=c3_migrator.<project-ref>;Password=<C3_MIGRATOR_PASSWORD>;SSL Mode=Require'`
   then `dotnet run --project NJTruckCharging.CloudCopy -- methodology-editing`. It runs in one transaction.
3. **Lock and verify (SQL Editor, as postgres).** Run `sql/04-lockdown.sql`, then `sql/05-check-isolation.sql`. Every row must be `OK`.
   Step 2 only adds columns and a table, so the row counts of all existing tables stay the same.
4. **Render: deploy the new API with editing still OFF.** Don't add the new variables yet. Check that `/healthz` answers `ok` and
   that the dashboard, CSV export and Methodology page load. `GET /api/methodology/editing` should answer `enabled: false`.
5. **Netlify: deploy the new dashboard.** No new Netlify variables are needed. Editing buttons stay hidden while step 6 is not done.
6. **Render: turn editing on** with two variables, saved together:
   * `ConnectionStrings__MethodologyWriter` = `Host=<pooler-host>;Port=5432;Database=postgres;Username=c3_methodology_writer.<project-ref>;Password=<C3_WRITER_PASSWORD>;SSL Mode=Require;Maximum Pool Size=2`
   * `Methodology__CloudEditing__Enabled` = `true`

   Leave `ConnectionStrings__DefaultConnection`, `ReadOnlyDemo__Enabled=true` and all `Auth__*` variables unchanged.
   The API refuses to start if the flag is on without a writer login, or if the writer login is the read-only login.
7. **Verify on production** with a throw-away draft:
   * a company account and the Gmail administrator can each create a draft, save it and review results;
   * a second browser saving an outdated copy gets the conflict message;
   * Delete the draft. Its rules stay in the audit trail.
   * `POST /api/saved-sites` still answers `403 read_only`, and an outsider Gmail account gets 403.

   Applying scoring changes everyone's scores, so test Apply/Restore only at an agreed time, and restore the previous version right after.

## 4. Rollback procedures

| Situation | Action | Effect |
|---|---|---|
| A published method is wrong | Methodology → Previous Methods → previous version → **Review Results** → **Restore** | Every site goes back to that version's scores and ranks in one transaction. History is kept. |
| Stop all editing now | Render: `Methodology__CloudEditing__Enabled=false`, or delete it → Save | After the restart, every methodology change answers `403 read_only`. Scores, versions and history are untouched and still readable. |
| Stop database writes immediately, even before Render restarts | SQL Editor: `ALTER ROLE c3_methodology_writer NOLOGIN;` then `SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = 'c3_methodology_writer';` | The writer cannot connect, so methodology changes fail with a generic error (500/503) and nothing changes. Undo with `ALTER ROLE c3_methodology_writer LOGIN;`. |
| Remove the feature from the database | 1. Turn editing off on Render. 2. SQL Editor: `ALTER ROLE c3_migrator LOGIN; GRANT USAGE, CREATE ON SCHEMA c3 TO c3_migrator;` 3. `CloudCopy methodology-editing-rollback`. 4. `sql/09-methodology-writer-role-rollback.sql`. 5. `sql/04-lockdown.sql`. 6. `sql/05-check-isolation.sql`: all OK. | Grants, policies, trigger and login are gone. **All data stays**: versions, live scores, activation history, audit trail and the audit columns. The new API keeps working read-only. |
| Roll back the API code | Redeploy the previous Render build | The previous build ignores the extra columns. Turn editing off first, because the old build cannot use the writer. |
| Remove the audit schema too | The commented block at the end of `10-methodology-editing-rollback.sql`, only after exporting `MethodologyChangeLog` | **Deletes the audit history.** No C3 data is affected. Only needed with an API build older than this feature. |

Rule-of-thumb: a bad **methodology** is fixed with Restore; a bad **feature** is switched off with the Render flag.

## 5. Configuration

| Setting | Default | Meaning |
|---|---|---|
| `Methodology__CloudEditing__Enabled` | unset (off) | only the exact value `true` turns editing on |
| `ConnectionStrings__MethodologyWriter` | unset | the writer login; required when editing is on in the cloud |
| `Methodology__RateLimits__ScoringPerWindow` | 6 | recalculations per user per window |
| `Methodology__RateLimits__ScoringWindowSeconds` | 600 | the window |
| `Methodology__RateLimits__EditsPerMinute` | 120 | draft creates/saves/deletes/validations per user per minute |

**Storage.** Each reviewed version keeps one proposed-score row per site, about 30,600 rows. The API keeps them only for the
baseline, the active version, the previous version and the 5 most recently reviewed others; it prunes older ones
automatically. A pruned version must be reviewed again before it can be published or restored.

## 6. Tests

* **Backend:** `dotnet test NJTruckCharging.slnx`. `MethodologyCloudEditingApiTests` covers the cloud configuration:
  * unauthorized, lookalike, unconfirmed, banned, deleted, signed-out and blocked users;
  * the Gmail exception;
  * cross-site requests;
  * read-only mode for every other change;
  * conflicts, including simultaneous saves;
  * invalid rules;
  * duplicate publish, overlapping jobs and rate limits;
  * a database failure with rollback;
  * full audit;
  * consistency of list, detail and CSV.

  `MethodologyEditingTests` covers start-up checks, the metadata-based read-only exception and the job gate.
* **Angular:** `npx ng test`: the editor conflict flow, read-only deployments and audit display.
* **Real PostgreSQL with the real roles:** `node e2e/methodology-editing/run-pg-e2e.mjs <deps> "<SQL Server test DB>"`. It builds
  today's production schema and runs the migration exactly as in §3. Then it:
  * probes the writer directly with forbidden statements;
  * drives the API on both logins;
  * injects a database failure;
  * rolls the feature back;
  * checks that every non-methodology table is byte-identical.
