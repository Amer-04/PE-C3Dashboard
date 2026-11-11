# Employee self-registration: design, rollout and operations

Status (2026-11-11): **implemented and tested locally only.** Nothing in Supabase, Render, Netlify or any database has
been changed. Every hosted step below waits for approval.

Goal: anyone who owns and confirms an address at **exactly** `poweredison.com` or `ev-edison.com` can create an account,
sign in and use the read-only dashboard. Nobody else can, and existing personal-address accounts do not gain or keep
access by accident.

## 1. How access is decided (every request, on the API)

| Gate | Where | Refused → |
|---|---|---|
| 1. **Token**: Supabase JWT signature (project JWKS, ES256/RS256), issuer, audience `authenticated`, expiry | `SupabaseAuth` (JwtBearer) | 401 |
| 2. **Account**: Supabase confirms *now* that the account and session exist, the e-mail is **confirmed**, the account is not banned or anonymous, and the id matches the token (`GET /auth/v1/user`, cached per token ≤ `Auth:Supabase:UserCheckSeconds`, default 60 s, max 300) | `SupabaseUserDirectory` | 401 (503 `auth_unavailable` if Supabase can't be reached; never access) |
| 3. **Access rule** `Auth:AccessRule` + `Auth:BlockedUserIds` | `EmployeeAccessHandler` | 403 `not_approved`, generic message |
| 4. **Read-only**: any non-GET/HEAD/OPTIONS | `Program.cs` (unchanged) | 403 `read_only` |

* Gate 2 runs only after gate 1, so forged tokens never reach Supabase. Its result is attached as a claim with this
  server's own issuer; a token cannot carry or fake it (tested).
* The domain rule uses the e-mail **Supabase confirmed** and requires the token's `email` claim to match it. A user who
  changes their address to a personal one loses access on the next check: the Before User Created hook does not run on
  e-mail changes, so the API is the gate that covers this.
* Domain rule (`EmployeeEmailPolicy`): exactly one `@`, non-empty local part, printable ASCII only, domain **equal** to
  `poweredison.com` or `ev-edison.com`, case-insensitive. Refused: subdomains, `poweredison.co`,
  `poweredison.com.evil.io`, trailing dot, Unicode homoglyphs, spaces, quoted local parts. The domains are in code, not
  configuration, so a mistyped variable can never widen access.
* The same rule exists three times (API, Supabase hook, Angular forms). All three are tested against one file:
  `NJTruckCharging.Web/src/app/core/employee-email.vectors.json`.

`Auth:AccessRule` values (anything else, including a lower-case typo or a number, stops the API from starting):

| Value | Who gets in | Startup requires |
|---|---|---|
| *(unset)* = `UserIds` | ids on `Auth:AllowedUserIds` (today's behaviour) | non-empty allowlist |
| `EmailDomainAndUserIds` | on the allowlist **and** (a confirmed company address **or** an administrator exception) | non-empty allowlist |
| `EmailDomain` | any confirmed company address, **or** an administrator exception | allowlist **absent** (so the config says what it does) |

### Administrator exceptions (`Auth:DomainExceptionUserIds`)

For administrators who have no company mailbox (e.g. the project administrator's Gmail account). Only the user ids listed
here may pass **without** a company address. Every other personal account is refused.
* **Only the domain test is waived.** The token checks (signature, issuer, audience, expiry), the Supabase account check
  (confirmed e-mail, not banned, not deleted, session alive), the token/confirmed e-mail match, `Auth:BlockedUserIds`
  and read-only mode all still apply. So a banned, deleted or signed-out administrator is refused exactly like anyone
  else, with the same timing and the same open questions about hosted Supabase (section 8).
* It is matched by **user id**, never by e-mail address. Another account cannot borrow it by claiming the
  administrator's address (tested).
* The Supabase hook is **unchanged**: it still refuses new Gmail sign-ups. The exception only lets an *existing*
  account through the API. Don't **delete** the administrator's account: while the hook is on, it can't be recreated
  with a Gmail address. To suspend the administrator, ban them instead (reversible).
* Startup refuses: exceptions with the `UserIds` rule (there is no domain test to waive), more than 3 exceptions, a
  malformed id, or an id that is also on `Auth:BlockedUserIds`.
* With `EmailDomainAndUserIds`, an exception must **also** be on `Auth:AllowedUserIds`.
* Revoking an exception: remove the id from `Auth__DomainExceptionUserIds` (403 once Render has restarted with the
  new value; this does not depend on Supabase), or ban the account in Supabase (401 once Supabase stops accepting it;
  see section 8 for the timing, which is not yet verified on the hosted project).

All modes also need `Auth:Supabase:PublishableKey` (public key; a `sb_secret_…` key stops startup). In the cloud
(PostgreSQL) mode the API still refuses to start without `Auth:Mode=Supabase` and read-only mode, so there is no setting
that switches authentication off.

## 2. Replacing `Auth__AllowedUserIds` safely

Each phase is separately reversible and is verified before the next. No phase removes authentication.

**Phase 1: deploy the code, behaviour unchanged.**
1. Supabase SQL Editor: run `NJTruckCharging.CloudCopy/sql/08-auth-users-inventory.sql` (read-only) with the current
   allowlist pasted in. Every allowlisted account must show `confirmed = true`; administrator-created accounts were
   created confirmed. Fix any that are not before continuing.
2. Render: add `Auth__Supabase__PublishableKey` (same public key Netlify uses). Leave `Auth__AccessRule` **unset**.
3. Deploy. Check `/healthz`, sign in as an approved user, and confirm a non-approved one still gets 403.
   *New in this phase:* deleted/banned accounts and signed-out sessions are designed to be refused within the 60 s
   cache instead of at token expiry (up to 1 hour). This depends on how hosted Supabase answers; see section 8. *Rollback:* redeploy the previous commit (the extra variable is ignored by old code).

**Phase 2: require company addresses for the people already approved.**
1. Re-run the inventory (script 08, with both values pasted). Each `LOSES ACCESS` row is an approved person using a
   personal address who is not an exception. Create a company-address account for them, or accept the loss and tell
   them first. Each administrator who must keep a personal address goes into `Auth__DomainExceptionUserIds` (at most 3)
   **and stays** on `Auth__AllowedUserIds`.
2. Render, in **one** save: `Auth__AccessRule=EmailDomainAndUserIds` and `Auth__DomainExceptionUserIds=<administrator
   user id(s)>`. Sign-ups are still **off** in Supabase.
3. Verify: the administrator (exception) still sees the dashboard; any other personal-address approved account →
   "does not have access"; company-address approved → dashboard.
   *Rollback:* in one save, clear `Auth__AccessRule` **and** `Auth__DomainExceptionUserIds` (exceptions with the
   original rule stop startup).


**Phase 3: open sign-ups in Supabase (API still requires the allowlist).** Section 3 below, in this order: custom SMTP →
templates and URLs → apply hook script 07 and enable the hook → password and e-mail settings → **then** "Allow new users
to sign up". Then test with a real colleague's address and with a personal address, which Supabase must refuse with
"Please use your Power Edison or EV Edison work e-mail address". New accounts can confirm and sign in at Supabase but get
403 from the API. Self-registration is fully exercised without granting anything yet.
*Rollback:* sign-ups off.

**Phase 4: switch to the domain rule.**
1. Re-run the inventory. Review every `GAINS ACCESS` row: each must be someone who should have access.
2. Render: `Auth__AccessRule=EmailDomain` and **delete** `Auth__AllowedUserIds`. **Keep** `Auth__DomainExceptionUserIds`.
   The API will not start with both rule and allowlist, so a half-done change can't go live. Render keeps the previous deploy if the new one fails its health check; confirm
   `/healthz` and the deploy status.
3. Netlify: `C3_SELF_REGISTRATION=true`, then rebuild. This shows the "Create account" tab; it is hidden until then.
4. Verify against section 7 of `CLOUD_DEPLOYMENT.md` plus: new company account → confirm link → dashboard; personal
   address → refused at sign-up; confirmed personal account (if any exists) → "does not have access".
   *Rollback:* `Auth__AccessRule=EmailDomainAndUserIds` with the old allowlist restored, and/or sign-ups off.

## 3. Supabase settings for phase 3 (not changed yet)

| Setting | Value | Why |
|---|---|---|
| Authentication → Emails → **SMTP Settings** | Custom SMTP (see §4) | the built-in sender cannot serve employees |
| Sign In / Providers → Email → **Confirm email** | **On** | no session until the address is proven |
| Email → **Secure email change** | **On** | changing the address needs both mailboxes |
| Email OTP / link expiry | 3600 s (default) or less | links are single-use and short-lived |
| **Password requirements** | Min length 12; letters + digits | matches the form; Supabase enforces it |
| **Leaked password protection** | On if the plan offers it | rejects known-breached passwords |
| **Allow anonymous sign-ins**; phone, social and SSO providers | **Off** | e-mail only |
| Authentication → **Hooks → Before User Created** | Postgres function `c3_auth.before_user_created` (script 07) | server-side domain gate at account creation; check it is offered on the project's plan |
| **URL Configuration** | Site URL = Netlify address; Redirect URLs = `https://<site>.netlify.app/**` only | confirmation links can only return to the dashboard |
| **Rate limits** | e-mails per hour sized for the team (e.g. 30) after custom SMTP; keep sign-up and sign-in limits | limits abuse and mail cost |
| Emails → **Confirm signup** template | default `{{ .ConfirmationURL }}`, wording "C3 Site Finder: confirm your work e-mail" | |
| **Allow new users to sign up** | On, **last** | |

Hook script facts (`sql/07-before-user-created-hook.sql`): creates schema `c3_auth` (not exposed through the Data API)
and one function with no table access. EXECUTE is granted only to `supabase_auth_admin`; PUBLIC, `anon`,
`authenticated` and `service_role` are revoked. It stops without changes if it was already applied, and an undo line is
included. **Staging check before relying on it:** confirm in the project that the hook also fires for dashboard-created
users. The API refuses them in any case.

## 4. Why custom SMTP is required

Self-registration needs e-mail: the confirmation link (and later password recovery) is the only proof that someone
owns the address. Supabase's built-in e-mail service is for trying the product out. It sends only to members of the
Supabase project's team, at a very small hourly limit, from a shared sender with no deliverability guarantees.
Employees' confirmation e-mails would not arrive.

Requirements for the custom SMTP:
* A sender on a company domain, e.g. `no-reply@poweredison.com`, through Microsoft 365 / Google Workspace SMTP relay or
  a transactional provider (Amazon SES, Postmark, Resend, SendGrid).
* SPF, DKIM and DMARC set for that sender's domain, otherwise company mail filters will quarantine the messages.
* SMTP credentials stored only in Supabase's SMTP settings (never in the repository, Render or Netlify). Use a dedicated
  account or API key that can only send.
* Raise the Auth e-mail rate limit to fit the team once custom SMTP is active.
* Corporate link scanners (e.g. Microsoft Defender Safe Links) may open the confirmation link before the person does.
  The address is still confirmed, because the scanner works inside the company mailbox, but the link is used up. The
  dashboard then says "That link is no longer valid… Sign in", and signing in works. Tested.

## 5. Password recovery plan (not implemented; needs §4 first)

Until then the screen says "Forgot your password? Please ask the administrator" (the administrator can ban/delete and
the person re-registers, or send a reset from the Users page once SMTP exists).

Planned flow:
1. "Forgot password?" form → `supabase.auth.resetPasswordForEmail(email, { redirectTo: <site>/reset-password })`, only
   for company-format addresses. The answer is always the same "if the address has an account, we sent a link" (no
   account enumeration). Supabase rate-limits it.
2. Change the **Reset Password** e-mail template to link to
   `{{ .SiteURL }}/reset-password?token_hash={{ .TokenHash }}&type=recovery`, not `{{ .ConfirmationURL }}`. This works
   on any device (PKCE links only work in the browser that asked), and the link does nothing until the person presses
   a button, so mail scanners can't use it up.
3. `/reset-password` page: on the button press, `verifyOtp({ type: 'recovery', token_hash })` → new password + repeat
   (same rules as sign-up) → `updateUser({ password })` → `signOut({ scope: 'global' })`, which ends every other session
   (e.g. an attacker's) → back to sign-in with "password changed". Remove the token from the address bar on load (as the
   confirmation link handling already does).
4. Recovery grants nothing by itself: the API still requires a confirmed company address on every request.
5. Tests: generic answer for unknown/known/personal addresses, expired and reused links, cross-device link, all
   other sessions refused by the API after the change (≤ cache time).

## 6. When someone leaves the company

Order matters: whoever controls the mailbox can re-register or reset the password.

1. **IT disables the company mailbox** (or removes the address).
2. Supabase → Authentication → Users → the person → **Ban user** (long duration). Prefer ban over delete: a banned
   account keeps the address taken, so it can't be registered again, and its sign-in, refresh and API access stop.
   Delete is fine once the mailbox is gone.
3. The API is designed to refuse their still-unexpired token at most `Auth:Supabase:UserCheckSeconds` (60 s) after
   Supabase stops accepting it. Locally, against the stand-in Supabase, it was refused after about 4 s with a 5 s
   setting, and the open dashboard returned to the sign-in screen. **Not yet measured on the hosted project**
   (section 8). Until it is, assume a banned person may keep access for up to their token's remaining life (≤ 1 hour).
   Use `Auth__BlockedUserIds` (step 4) when that is not acceptable.
4. If Supabase can't be used at that moment, add the user id to Render's `Auth__BlockedUserIds` (checked before any
   rule; takes effect when Render restarts).
5. Record it, then re-run the inventory: the account shows `banned_until` in the future.
6. Monthly: run the inventory and compare company-domain accounts and `last_sign_in_at` with HR's leavers list.

What this cannot do: a person who still controls a company mailbox is, by design, an employee to this system. The HR/IT
mailbox process is the real off-boarding control.

## 7. Session handling (unchanged, plus)

* Tokens are kept by supabase-js in the browser's storage and sent only to the API (tested). Sign out ends the session
  in Supabase (`/logout`), clears storage and reloads. Against the local stand-in, the API refused that token within the
  cache time; on hosted Supabase this is still to be verified (section 8).
* `flowType: 'pkce'` and `detectSessionInUrl: false`: e-mailed links carry a one-time code, never an access token, and
  never sign anyone in. They are removed from the address bar and history.
* Generic messages: the same text for unknown e-mail and wrong password; "check your e-mail" whether or not the address
  already has an account; "does not have access" without saying why.

## 8. Ban, sign-out and session revocation: how each is verified, and the cache limit

**Mechanism.** The API never decides "still allowed" from the token alone. For each distinct access token it asks
Supabase `GET /auth/v1/user` (with that token). Any of these refuses the request with 401:
* the answer is 401/403/404, which is what Supabase returns when the user is deleted or the token's session no longer
  exists (signed out, or revoked);
* `banned_until` is in the future, or can't be read;
* `email_confirmed_at` is empty, the account is anonymous, or the returned id differs from the token's `sub`.

A network error, timeout, 429 or 5xx gives 503 `auth_unavailable`, never access. These answers are never cached.

**Status: implemented and tested locally; not yet proven against hosted Supabase.** The API side is certain: once
Supabase's answer for a token says "no", that token is refused, and a "yes" is reused for at most the cache time. What
is **not** yet proven is what hosted Supabase answers after a ban, a deletion or a sign-out. Every timing below is
therefore conditional until the checks at the end of this section have been done and recorded.

**The cache limit, precisely (API side).**
* An answer is cached in the API's memory **per token** (keyed by SHA-256 of the token; raw tokens are not stored) for
  `Auth:Supabase:UserCheckSeconds`: default **60 s**, capped at 300 s, 0 = ask on every request.
* Worst case, **if** hosted Supabase refuses the token from the moment of the ban, sign-out or deletion: a token
  checked and found valid just before keeps working for up to 60 s on the API. A refreshed or new token is a different
  key and is checked afresh. Supabase is expected to refuse refreshing or signing in a banned, deleted or signed-out
  account (also to be confirmed).
* **If** hosted Supabase keeps answering "valid" for such a token, the API falls back to the token's expiry (up to
  1 hour). The exception is a ban: the API also reads `banned_until`, so a ban still works if Supabase returns it.
  `Auth__BlockedUserIds` does not depend on Supabase at all.
* The cache lives in one API process and is emptied when it restarts. With more than one Render instance, each
  instance has its own cache; the per-instance limit is still the cache time.
* Without this check (the behaviour before this change), the limit was the token's remaining life: up to **1 hour**
  (Supabase's default JWT expiry).
* If 60 s is too long, lower `Auth__Supabase__UserCheckSeconds`. Each new token then costs one more call to Supabase.

**What is verified, and where.**

| Case | Automated test (local) | Result | Still to confirm on the hosted project (phase 3) |
|---|---|---|---|
| Account **deleted** while signed in | xUnit `Revocation_…`, E2E "delete" | next request 401 (xUnit, no cache); E2E: refused after 3.8 s with a 5 s cache; the open dashboard returns to sign-in; signing in again refused | that `/auth/v1/user` answers 401/403/404 for a deleted user |
| Account **banned** while signed in | xUnit (fake `Banned`), unit `Reads_confirmation_and_ban_state`, E2E "ban" | 401; E2E refused after 3.8 s with a 5 s cache; sign-in shows "disabled" | that `/user` either refuses or returns `banned_until` for a banned user (both are handled) |
| **Sign out** in the dashboard | E2E, both domains | stored session cleared at once; the same token refused by the API within the 5 s test cache | that `signOut({ scope: 'local' })` revokes the session server-side, so `/user` refuses it |
| One **session revoked**, account kept | xUnit `RevokeSession` | 401 | covered by the sign-out check |
| E-mail **unconfirmed** / changed to personal | xUnit, E2E | 401 / 403 | none |
| **Supabase unreachable** | xUnit, unit tests | 503, no data, nothing cached | none |
| **Blocklist** (`Auth__BlockedUserIds`) | xUnit | 403 after restart with the setting | none |

The E2E Supabase is a local stand-in written to behave like Supabase Auth, so the right-hand column must be checked
once on the real project before any timing is relied on. Procedure (with a disposable test account):
1. Sign in on the dashboard. Copy the access token from the browser's storage and note the time.
2. Ban the account in Supabase → Users and note the time.
3. Every 10 s call `curl -H "Authorization: Bearer <token>" https://<service>.onrender.com/api/sites?pageSize=1` until
   it no longer answers 200 (or 403 for an account that is not approved). **Record** the elapsed time and status.
   Expected: 401 within about 60 s. If it is still accepted after 2 minutes, stop and report: revocation then relies on
   token expiry (≤ 1 hour) and `Auth__BlockedUserIds`.
4. Repeat with Delete user and with Sign out (copy the token before signing out).
5. Do the same for an administrator exception only with a disposable exception account, never by banning or deleting
   the real administrator.
6. Record the results here (date, case, elapsed seconds, status). Until then, the statements above are design
   intentions, not production evidence.

| Date | Case | Elapsed until refused | Status seen | By |
|---|---|---|---|---|
| (not yet tested on hosted Supabase) | | | | |

## 9. Deployment order: Render must never start with missing configuration

What makes the API refuse to start (in the cloud/PostgreSQL mode, before anything is served):
`Auth:Mode` not `Supabase`; read-only mode off; `Auth:Supabase:PublishableKey` missing or a `sb_secret_…` key;
`Auth:AccessRule` not exactly `UserIds`, `EmailDomainAndUserIds` or `EmailDomain`; `UserIds`/`EmailDomainAndUserIds`
with an empty allowlist; `EmailDomain` with an allowlist still present; a malformed id in any list; administrator
exceptions with the `UserIds` rule, more than 3 of them, or one that is also blocked. A refused start
cannot serve requests, so a configuration mistake can never mean open access. On Render a failed deploy normally leaves
the previous deploy serving; check the deploy log each time rather than assuming it.

Order (each step is checked before the next):
1. **Before deploying the new code**, add `Auth__Supabase__PublishableKey` on Render. The currently running code ignores
   it. Do **not** set `Auth__AccessRule` yet.
2. Run the inventory (script 08). Every account in `Auth__AllowedUserIds` must be confirmed and not banned, so
   **existing company-approved users keep access** after the deploy.
3. Deploy the new code (auto-deploy is off; Manual Deploy). Check `/healthz`; an approved user sees the dashboard; an
   unapproved one gets 403.
4. Netlify: leave `C3_SELF_REGISTRATION` unset. The dashboard still shows only Sign in.
5. Phase 2: in one save, set `Auth__AccessRule=EmailDomainAndUserIds` and `Auth__DomainExceptionUserIds` (allowlist
   unchanged; the administrator must be on both).
6. Phase 3: Supabase changes (section 3), sign-ups **last**.
7. Phase 4: change `Auth__AccessRule=EmailDomain` **and** delete `Auth__AllowedUserIds` in the **same** save. If they
   are saved separately, the in-between state (`EmailDomain` with an allowlist, or `UserIds` with none) is refused at
   startup. That fails closed, but makes a failed deploy. Then set `C3_SELF_REGISTRATION=true` on Netlify and rebuild.

`render.yaml` is unchanged. Variables set in the Render dashboard are not removed by it, but it does not list
`Auth__Supabase__PublishableKey`, `Auth__AccessRule` or `Auth__DomainExceptionUserIds`. Add them to the Blueprint (as `sync: false`) when Render
configuration changes are approved.

**C3 data and existing users.** This change adds no database migration, writes nothing and keeps read-only mode: every
non-GET request is still refused (tested for self-registered users too). Until phase 4, the people on
`Auth__AllowedUserIds` are the only ones with access, exactly as today. All local testing used the LocalDB test
databases (`NJTruckCharging_CloudAuthTest`, `NJTruckCharging_DomainAuthTest`) and a throw-away PostgreSQL. It never
used the live `NJTruckCharging` database or Supabase.

## 10. Tests

| Suite | Command | Covers |
|---|---|---|
| API unit + integration (xUnit, LocalDB) | `dotnet test NJTruckCharging.slnx -c Release` | domain vectors; Supabase user check (headers, status mapping, ban/unconfirmed parsing, cache, coalescing, outage not cached); forged verified-email claims; both domains; forbidden domains; unconfirmed; deleted/banned/signed-out; e-mail changed to personal; forged tokens never reach Supabase; Supabase down → 503; blocklist; transition rule; unsafe configurations refuse to start; read-only kept; administrator exceptions (Gmail admin allowed by id, other personal accounts refused, cannot be borrowed, revoked by ban/delete/sign-out/removal/blocklist, transition rule, unsafe exception configs) |
| Angular (vitest) | `cd NJTruckCharging.Web; npx ng test --watch=false` | domain vectors; password rules; sign-up/resend generic answers; hook refusal message; confirmation-link handling; PKCE options; tab hidden until enabled; 401/403/503 handling |
| Supabase hook (local PostgreSQL) | `node NJTruckCharging.CloudCopy/sql/tests/test-07-hook.mjs <deps>` | every vector against the real SQL; execute rights; idempotency; inventory script |
| End to end (local only) | `node e2e/self-registration/run-e2e.mjs <deps> <web build> [shots]` | real API + production dashboard in Chrome + mock Supabase Auth calling the real hook: create → unconfirmed refused → link → sign in → data → read-only → sign out, for both domains; forbidden domains at form and hook; personal account → no access; forged tokens; unconfirmed at API; revocation by delete and ban; Gmail administrator exception: dashboard, CSV, read-only, other Gmail refused, hook still refuses Gmail sign-up, banned administrator refused |

`<deps>` is a scratch folder with `npm i pg embedded-postgres puppeteer-core@23`. The mock Supabase Auth
(`e2e/self-registration/mock-supabase-auth.mjs`) is a local test double only. It does not prove details of the hosted
service (exact error codes, whether the hook fires for dashboard-created users), so check those in phase 3.
