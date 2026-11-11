# C3 Site Finder — Release Checklist & Operations

Prepared for the business-owner demonstration (branch `feature/saved-sites-pipeline`). **Not pushed or deployed** —
both require explicit approval.

## 1. Release checklist (run on 2026-11-02)

| Check | Command | Result |
|---|---|---|
| Backend build (Release) | `dotnet build NJTruckCharging.slnx -c Release` | ✅ 0 warnings, 0 errors |
| Backend tests | `dotnet test NJTruckCharging.slnx` | ✅ 87 / 87 (unit + LocalDB integration) |
| Angular production build | `cd NJTruckCharging.Web && npx ng build` | ✅ 1.48 MB initial (354 kB transferred), within budgets |
| Angular tests | `npx ng test --watch=false` | ✅ 24 / 24 |
| Migrations | `dotnet ef migrations list` / `has-pending-model-changes` (from `NJTruckCharging.Infrastructure`, `--startup-project ../NJTruckCharging`) | ✅ 10 migrations, all applied to the dev DB (latest `AddSiteScreening`); no pending model changes |
| Dependency vulnerabilities | `dotnet list NJTruckCharging.slnx package --vulnerable --include-transitive`; `npm audit` | ✅ NuGet: none. npm: 0 (after `overrides` for `fflate` ≥ 0.8.3 and `image-size` ≥ 2.0.4) |
| C3 dataset integrity | Snapshot `SiteId, Segment, Score, ComputedRank, FileScore, OpenAcresEst, FreightUse, PropClass` for all rows and diff against the pre-change baseline | ✅ 30,584 rows byte-identical; workbook SHA-256 `ac7fbb79…f806c` unchanged |
| Real-record validation | `node C:\Users\shaha\njtc-verify\validate-real.mjs` (API on :5172) | ✅ 384 / 384 checks on 36 real sites (ranks 1, 2, 10, 50, 250 and last, in all 6 segments) |
| Browser checks | `verify-compare.mjs`, `verify-screening.mjs`, `demo-walkthrough.mjs` (puppeteer-core) | ✅ all pass — Light/Dark, 390 px phone width |
| Backup + restore | see §2 | ✅ backup verified with CHECKSUM; restored copy matched the live DB exactly |

## 2. Database backup & restore (SQL Server LocalDB)

The database holds the imported C3 dataset **and** user-owned research (saved sites, contacts, feasibility reviews,
screening decisions). The dataset can be re-imported from the workbook; the research cannot — back it up.

**Back up** (stop nothing; online backup is safe):

```powershell
mkdir C:\Users\shaha\NJTruckCharging\backups -Force
$f = "C:\Users\shaha\NJTruckCharging\backups\NJTruckCharging_$(Get-Date -Format yyyyMMdd_HHmm).bak"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "BACKUP DATABASE [NJTruckCharging] TO DISK = N'$f' WITH INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$f' WITH CHECKSUM;"
```

Take one **before every workbook re-import, migration, or demo**. `backups/` and `*.bak` are git-ignored — copy the
file somewhere off this machine (e.g. a company drive) for real protection.

**Restore** (stop the API first — it holds connections):

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d master -b -Q "ALTER DATABASE [NJTruckCharging] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [NJTruckCharging] FROM DISK = N'<path-to.bak>' WITH REPLACE, RECOVERY; ALTER DATABASE [NJTruckCharging] SET MULTI_USER;"
```

To test a backup without touching the live DB, restore it under another name with
`WITH MOVE 'NJTruckCharging' TO '...\X.mdf', MOVE 'NJTruckCharging_log' TO '...\X_log.ldf'`, compare, then drop it.

Pre-release backup taken: `backups\NJTruckCharging_20261102_prerelease.bak` (30,584 sites, 3 saved sites,
10 feasibility items, 7 contacts, 0 screening decisions).

## 3. Running the demo

```powershell
# terminal 1 — API (applies migrations on start), http://localhost:5172
cd NJTruckCharging\NJTruckCharging ; dotnet run --launch-profile http
# terminal 2 — dashboard, http://localhost:4200
cd NJTruckCharging\NJTruckCharging.Web ; npx ng serve
```

Demo path (verified end-to-end by `demo-walkthrough.mjs`):
1. Dashboard (Light Mode) — 30,584 candidates, map, KPIs.
2. **Screening** → choose a segment (selector or the per-segment progress tiles).
3. Ranks → *Top 10 per segment*.
4. Review site #1: embedded satellite view, **Satellite in Google Maps**, **Street View**, property facts.
5. Record a decision (Promising / Needs more research / Unsuitable) — evidence is required.
6. **Next ›** to screen sequentially; segment progress updates (e.g. 3 of 10 reviewed).
7. **☆ Save** to bookmark promising candidates (appear under Saved).
8. **+ Compare** 2–5 sites → **Compare** page.
9. **Export screened sites** (CSV with decisions + evidence) or **Export queue CSV**.

## 4. Secrets & configuration

- **No credentials in source control.** Google Maps key: `dotnet user-secrets` locally, `GoogleMaps__ApiKey`
  environment variable when deployed. Templates: `docs/config/secrets.example.json` and
  `docs/config/deployment.env.example`. `SecretScanTests` fails if a Google API key appears in any committable file.
- **Admin endpoints** (`/api/admin/*`) are open only in Development. Any other environment returns 403 unless
  `Admin:ApiKey` is configured and sent as `X-Admin-Key` (covered by `ProductionAdminTests`).
- **Fallback basemap** (no Google key): OpenStreetMap standard tiles. Fine for local/demo use with attribution,
  not for heavy production traffic. CARTO's free tiles now require a key and were replaced.

## 5. Before the first push

1. **Rotate the Google Maps key** that was committed to `appsettings.Development.json` in commits `d43ebdb`
   through `65622a5`. **Correction (2026-11-11):** `d43ebdb` is in the history of `origin/main`
   (shahadkhraisheh96/NJTruckCharging), so treat the key as exposed; it is not in `Amer-04/PE-C3Dashboard`. In Google Cloud → APIs &
   Services → Credentials, create a replacement key restricted to *Websites* (`http://localhost:4200/*` plus the
   production URL) and to *Maps JavaScript API* only. Then run
   `dotnet user-secrets set "GoogleMaps:ApiKey" "<new key>"` and delete the old key in Google Cloud.
2. **Clean the history** (see §6). Once the old key is deleted it is useless, but removing it from history
   is still recommended before the branch becomes public.
3. Re-run §1, including `dotnet test --filter SecretScanTests`.

## 6. Git history cleanup plan (needs approval; rewrites local commits)

The key exists only in local commits on `feature/saved-sites-pipeline`. `origin` (GitHub) has `main` only, and it
is clean, so no one else has these commits. Rehearsed on a throwaway clone on 2026-11-02 with the results below.

```powershell
# 0. Safety copy (keeps the original history recoverable; delete it after step 5 because it holds the old key)
git clone --mirror C:\Users\shaha\NJTruckCharging C:\Users\shaha\NJTruckCharging-history-backup.git

# 1. Tool + replacement rule. Keep the rule file OUTSIDE the repo; it contains the old key.
python -m pip install git-filter-repo
#    C:\Users\shaha\replacements.txt, one line:   <OLD_KEY>==>REMOVED_GOOGLE_MAPS_KEY

# 2. Rewrite ONLY the commits that are not on main. Do not omit --refs: GitHub's main commit is GPG-signed, and an
#    unscoped run strips the signature, changes main's hash and would force a force-push.
git filter-repo --replace-text C:\Users\shaha\replacements.txt --refs main..feature/saved-sites-pipeline --force

# 3. Verify (prints commit ids only):
git log --branches -S"<OLD_KEY>" --oneline      # expect no output
git merge-base --is-ancestor origin/main feature/saved-sites-pipeline; echo $LASTEXITCODE   # expect 0
dotnet test NJTruckCharging.slnx                 # expect all green

# 4. filter-repo removes the 'origin' remote. Re-add it before pushing:
git remote add origin https://github.com/shahadkhraisheh96/NJTruckCharging

# 5. Delete replacements.txt and the mirror backup.
```

Rehearsal results: the key was removed from every branch. `main` (15c97bb) was unchanged and GitHub's
`6b2d571` stayed an ancestor (a normal push, no force). 23 feature commits remained: the 15 before `d43ebdb`
kept their hashes and the 6 from `d43ebdb` onward got new hashes. The tip's only difference was
`appsettings.Development.json`.

Also delete copies of the old value outside git: rebuild to refresh the `bin/` outputs, and remove the earlier
Claude Code session transcript under `~/.claude/projects/C--Users-shaha-NJTruckCharging/` that contains it.

## 7. Known issues

- Rotating the exposed key is still pending; it needs Google Cloud access (§5).
- The app has no login by design. Protect any hosted deployment at the network layer (README → Access &
  deployment security).
