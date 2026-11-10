# C3 Site Finder — online demo (dedicated Supabase project + Render + Netlify)

Read-only demo for approved company users. Free plans only.

| Part | Where | Holds |
|---|---|---|
| Database | Supabase project **C3-Site-Finder** `https://olxmmztusojqfyvnzusx.supabase.co` (dedicated to C3), schema **`c3`** | sites, scores, methodology versions, bookmarks, contacts, research |
| Sign-in | Supabase Auth of the same project | approved users only; e-mailed one-time code |
| API | Render (Docker, free) | ASP.NET Core API; checks every request |
| Website | Netlify (free) | Angular dashboard |

No other Supabase project is used or changed.

## Security model

* **Sign-in:** e-mail one-time code. Public sign-up is off; the website never creates accounts (`shouldCreateUser: false`).
  Users exist only because an administrator created them.
* **Every API request** needs a Supabase access token. The API checks signature (the project's published signing keys),
  issuer, audience (`authenticated`) and expiry → **401**. The token's user id must be in `Auth__AllowedUserIds` on
  Render → otherwise **403**. E-mail addresses and anything the browser says are never used for access.
* **Read-only:** the API refuses every POST/PUT/PATCH/DELETE (**403**) — bookmarks, contacts, research, screening,
  custom fields, scoring drafts, Review Results, Apply Scoring, Restore, imports.
* **Not exposed through Supabase's Data API:** the Data API is switched off; independently, schema `c3` grants nothing
  to `anon`, `authenticated`, `service_role` or `PUBLIC`, and row-level security is on for every table. A Supabase
  token or the publishable key therefore reads nothing directly from the database.
* **Database roles (least privilege):**
  * `c3_migrator` — owns schema `c3` and nothing else; used only for the import, then locked (`NOLOGIN`).
  * `c3_runtime_readonly` — the API's login: `USAGE` + `SELECT` on `c3` only, read-only transactions, 60 s statement
    timeout, 10 connections max, `SELECT`-only row-level-security policies.
* **Secrets:** passwords live only in a password manager, the operator's environment variables and Render's
  environment. The role-setup SQL contains only SCRAM verifiers (one-way hashes). The browser receives only public
  values (API address, project URL, publishable key); the Netlify build fails if a secret or service-role key is given.
* HTTPS + HSTS, strict Content-Security-Policy, CORS limited to the Netlify address, no OpenAPI/debug endpoints,
  `/healthz` is the only anonymous endpoint. The cloud API refuses to start without sign-in, allowlist and read-only mode.

## 1. Project (created 2026-11-10)

* **C3-Site-Finder**, project ref `olxmmztusojqfyvnzusx`, Free plan, region **us-east-1 (East US, North Virginia)** —
  verified from outside: the database address belongs to AWS us-east-1. Signing key: one **ES256** key published at
  `/auth/v1/.well-known/jwks.json` (compatible with the API).
* **Why Virginia:** the API makes 1–8 database round trips per request, one after another (measured: site list 6,
  site details 8, dashboard summary 3, map 1). With the database and Render both in Virginia each round trip is
  ~1–2 ms; a far-away database (e.g. Sydney, ~200 ms per round trip) would add 1–2 s to every page.
* No add-ons (IPv4 address, compute upgrades): the API and the copy tool connect through the free **session pooler**.
* Project Settings → **Data API: disabled**. If it must stay on, the exposed schemas must not include `c3`, and
  "Automatically expose new tables" must be off.

## 2. Configure Authentication

| Setting (Dashboard) | Value |
|---|---|
| Authentication → Sign In / Providers → **Allow new users to sign up** | **Off** |
| Authentication → Sign In / Providers → **Allow anonymous sign-ins** | **Off** |
| Email provider | **On**; Confirm email **on**; **Email OTP expiration 600 s**; **Email OTP length 6** |
| Phone and all social/SSO providers | Off |
| Authentication → Emails → Templates → **Magic Link** | Subject `Your C3 Site Finder sign-in code`; body: `<p>Your C3 Site Finder sign-in code is <b>{{ .Token }}</b>.</p><p>It expires in 10 minutes. If you did not ask for it, ignore this e-mail.</p>` |
| Authentication → Emails → **SMTP Settings** | **Custom SMTP** (company mail server or an e-mail provider). Without it Supabase sends only to the organization's team members, a few per hour. |
| Authentication → **Rate Limits** | E-mails per hour ≥ 30 (possible only with custom SMTP); keep OTP verification limits at default |
| Authentication → **URL Configuration** | Site URL = `https://<site>.netlify.app`; Redirect URLs: the same address (nothing else) |
| Project Settings → **JWT Keys** | Keep the default asymmetric signing key (ES256/RS256). Do not switch to a legacy shared secret. |
| Project Settings → **Data API** | **Disabled** (if it is on: exposed schemas must not include `c3`) |

**Approved users:** Authentication → Users → **Add user → Create new user**: work e-mail, a long random password that is
never shared (sign-in uses e-mailed codes), **Auto Confirm User: on** (no e-mail is sent). Copy each **User UID** into
`Auth__AllowedUserIds` on Render. **Revoke:** remove the UID (effective after Render restarts) and delete the user.

## 3. Copy source (local, read-only for the live database)

Never copy from or migrate the live `NJTruckCharging` database. Prepared and verified on 2026-11-10:

1. `BACKUP DATABASE NJTruckCharging TO DISK='...\backups\NJTruckCharging_20261110_cloud-source.bak' WITH COPY_ONLY, CHECKSUM`
   (only reads the live database); `RESTORE VERIFYONLY ... WITH CHECKSUM` → valid.
2. Restored as `NJTruckCharging_CloudSource_20261110`; fingerprints identical to live on all 14 tables; the pending
   migration `AddResearchEvidenceHistory` applied **to the copy only**.
3. Rehearsal into a local PostgreSQL 17 configured like Supabase (non-superuser admin, Supabase API roles): all 18
   tables verified identical (30,584 sites, 30,584 stored scores), isolation check all OK.

## 4. Create and fill schema c3 (each step needs approval)

On the operator's PC, from `NJTruckCharging/`. Two new random passwords (32+ characters, letters/digits/`-_.`), kept
in a password manager, set only as environment variables in that PowerShell window:

```powershell
$env:C3_MIGRATOR_PASSWORD = '<new random>'
$env:C3_READONLY_PASSWORD = '<new random>'
dotnet run --project NJTruckCharging.CloudCopy -- inventory-sql    # read-only check of the new project (optional)
dotnet run --project NJTruckCharging.CloudCopy -- bootstrap-sql > $env:TEMP\c3-step1.sql
```

**Step 1 (SQL Editor, as postgres):** paste `c3-step1.sql` (= `sql/01-roles-and-schema.sql` with SCRAM verifiers)
and run it; then delete the file. It stops without changes if `c3` or a `c3_*` role already exists.

**Steps 2–3 (operator PC, as c3_migrator through the session pooler; host from Dashboard → Connect → Session pooler):**

```powershell
$env:C3_SOURCE = 'Server=(localdb)\MSSQLLocalDB;Database=NJTruckCharging_CloudSource_20261110;Trusted_Connection=True;TrustServerCertificate=True'
$env:C3_TARGET = "Host=<pooler-host>;Port=5432;Database=postgres;Username=c3_migrator.olxmmztusojqfyvnzusx;Password=$env:C3_MIGRATOR_PASSWORD;SSL Mode=Require"
dotnet run --project NJTruckCharging.CloudCopy -- create-schema   # schema c3 + 18 tables (sql/02-c3-tables.sql)
dotnet run --project NJTruckCharging.CloudCopy -- copy            # refuses unless every c3 table is empty
dotnet run --project NJTruckCharging.CloudCopy -- verify          # must print: VERIFIED: every table is identical.
dotnet run --project NJTruckCharging.CloudCopy -- secure          # sql/03-runtime-grants.sql
```

**Step 4 (SQL Editor, as postgres):** run `sql/04-lockdown.sql`, then `sql/05-check-isolation.sql` (every row `OK`) and
`sql/06-auth-exposure-assessment.sql` (section 1: `anon`/`authenticated` have no access to anything in `c3`).

## 5. Render (API)

1. **New → Blueprint**, repository `Amer-04/PE-C3Dashboard`, branch `main` (after the pull request is merged).
   `render.yaml`: service `c3-site-finder-api`, Docker, free plan, region Virginia, auto-deploy off.
2. Secret environment variables:

| Key | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=<pooler-host>;Port=5432;Database=postgres;Username=c3_runtime_readonly.olxmmztusojqfyvnzusx;Password=<C3_READONLY_PASSWORD>;SSL Mode=Require;Maximum Pool Size=5` |
| `Auth__Supabase__Url` | `https://olxmmztusojqfyvnzusx.supabase.co` |
| `Auth__AllowedUserIds` | approved User UIDs, comma-separated |
| `Cors__Origins__0` | `https://<site>.netlify.app` |
| `GoogleMaps__ApiKey` | leave empty (OpenStreetMap; avoids Google charges) |

3. **Manual Deploy**. `https://<service>.onrender.com/healthz` → `ok`. Free services sleep after 15 idle minutes.

## 6. Netlify (website)

1. **Import from GitHub** `Amer-04/PE-C3Dashboard`; `netlify.toml` defines the build.
2. Environment variables (public values only): `C3_API_BASE_URL` = `https://<service>.onrender.com`,
   `SUPABASE_URL` = `https://olxmmztusojqfyvnzusx.supabase.co`, `SUPABASE_PUBLISHABLE_KEY` = the project's **publishable**
   key (Project Settings → API Keys) — never the secret / service-role key.
3. Deploy; then set Render's `Cors__Origins__0` and Supabase's Site URL / Redirect URL to the Netlify address.
4. Optional: **Stop builds** after the first deploy so later pushes deploy only on request.

## 7. Verify against the real services

* Website signed out → sign-in screen only. `curl https://<service>.onrender.com/api/sites` and `/api/sites/export` → 401.
* An e-mail that was not created in Supabase gets no code; a created user who is not in `Auth__AllowedUserIds` sees
  "not approved" (403).
* Approved user → dashboard with 30,584 sites, CSV export works, any change → "This online demo is read-only".
* Supabase Data API: `https://olxmmztusojqfyvnzusx.supabase.co/rest/v1/` with the publishable key returns nothing from `c3`
  (disabled, or schema not exposed).
* `sql/05-check-isolation.sql` → all OK.

## Expected charges

$0: Supabase Free (one free project; ~65 MB of the 500 MB database; free projects pause after about a week without
activity — restore in one click), Render Free, Netlify Free, OpenStreetMap, no Google key, no Supabase add-ons.
