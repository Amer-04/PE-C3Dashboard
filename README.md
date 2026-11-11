# NJTruckCharging — C3 Site Finder (EV Edison · Power Edison)

A dashboard for picking sites for medium and heavy-duty truck charging hubs to propose under the
**NJDEP C3 Medium & Heavy-Duty EV Charging Infrastructure NOFA** (proposals due **Dec 23, 2026**).
It loads the 30,584 screened candidate parcels from `C3_candidate_sites_all_segments.xlsx`. It re-scores
them with the C3 site-scoring algorithm and shows them on a Google Map, together with EV Edison /
Power Edison's own sites.

```
NJTruckCharging/
├─ data/C3_candidate_sites_all_segments.xlsx      ← seed workbook (imported on first run; NOT in this repo — see below)
└─ NJTruckCharging/
   ├─ NJTruckCharging.slnx
   ├─ NJTruckCharging.Domain/          entities + scoring engine (no dependencies)
   ├─ NJTruckCharging.Application/     filters, queries, dashboard stats, import orchestration
   ├─ NJTruckCharging.Infrastructure/  EF Core (SQL Server), migrations, streaming .xlsx reader
   ├─ NJTruckCharging/                 ASP.NET Core Web API (.NET 10)
   └─ NJTruckCharging.Web/             Angular 22 dashboard (Google Maps + deck.gl)
```

## Run it

Prerequisites: .NET 10 SDK, SQL Server LocalDB (it comes with Visual Studio), Node 20+.

**1. API** (http://localhost:5172)

```powershell
cd NJTruckCharging\NJTruckCharging
dotnet run --launch-profile http
```

On first start, the API:

- creates the `NJTruckCharging` database in LocalDB (EF Core migration `InitialCreate`);
- imports `data/C3_candidate_sites_all_segments.xlsx`, which takes about 20 seconds.
  The workbook is not published in this repository. Get it from the EV Edison team and place it at
  `data/C3_candidate_sites_all_segments.xlsx` before the first run.

To use another SQL Server, change `ConnectionStrings:DefaultConnection` in `appsettings.json`
(or set it with user-secrets).

**2. Dashboard** (http://localhost:4200)

```powershell
cd NJTruckCharging\NJTruckCharging.Web
npm install
npm start
```

`proxy.conf.json` forwards `/api` to the API, so no CORS setup is needed in development.

## Google Maps API key

The map works without a key: it falls back to the open OpenStreetMap basemap (light use with attribution only;
see the [OSM tile policy](https://operations.osmfoundation.org/policies/tiles/)). To get Google
Maps, including the satellite view that is useful for checking open land, add a key:

1. Go to https://console.cloud.google.com/. Create (or pick) a project and enable billing.
   The Maps JavaScript API has a monthly free tier.
2. Open **APIs & Services → Library** and enable **Maps JavaScript API**.
3. Open **APIs & Services → Credentials** and choose **Create credentials → API key**.
4. Restrict the key:
   - **Application restrictions:** *Websites*. Add `http://localhost:4200/*` (development key) or the dashboard
     address, e.g. `https://c3dashboard.netlify.app/*` (production key).
   - **API restrictions:** *Maps JavaScript API* only.
5. Optional: create a **Map ID** under **Google Maps Platform → Map management** (JavaScript, Vector)
   for custom styling. Otherwise `DEMO_MAP_ID` is used.
6. Give the key to the API. **Never put a key in `appsettings*.json`**: those files are committed, and a test
   (`SecretScanTests`) fails if a Google API key appears in any committable file.

   **Local development: user-secrets** (stored in your user profile, outside the repo):

```powershell
cd NJTruckCharging\NJTruckCharging
dotnet user-secrets set "GoogleMaps:ApiKey" "AIza..."
dotnet user-secrets set "GoogleMaps:MapId" "your-map-id"   # optional
```

   **Deployed: environment variables** (`__` maps to `:`), e.g. `GoogleMaps__ApiKey=...`. A full template is in
   [`docs/config/deployment.env.example`](docs/config/deployment.env.example); the user-secrets shape is in
   [`docs/config/secrets.example.json`](docs/config/secrets.example.json). Local secret files (`secrets.json`,
   `.env*`, `appsettings.Local.json`, `appsettings.*.Local.json`) are git-ignored.

The browser reads the key from `GET /api/config/maps`. Browser keys are always visible to users, so
the referrer and API restrictions in step 4 are what protect the key. Use a separate key per environment so one
can be rotated without affecting the other.

## What the dashboard does

- **Map:** shows all candidates (or the filtered set), colored and sized by score. Flood-zone sites have
  a red ring. EV Edison / Power Edison sites appear with their 2-mile rings. Hover a site for a summary;
  click it to open the details.
- **Filters:** segment, top N per segment, minimum score, property class, open acres, miles to the exit,
  trucking on site, county, interstate and utility. You can also exclude flood, contaminated, Highlands,
  busy multi-tenant sites and sites within 2 mi of a Power Edison site, or search by address, business or
  site id.
- **KPIs, segment cards and charts:** all of them follow the filters.
- **Site detail:** the full score breakdown, with the reason for every point. It also shows parcel and
  valuation data, truck demand, the owner mailing address, and the 10 largest fleets within 5 mi with
  phone and email. Links open Google Maps and Street View.
- **Ranked table:** sortable and paged, with a **Save / Unsave** button on every row and a **CSV export**
  of the filtered sites for outreach.
- **Saved sites (`/saved`):** the outreach pipeline. Each saved candidate moves through
  Shortlisted → Screening → Contacted → Transferred → Rejected, with notes and an assignee. The site
  detail panel also saves a site and shows its current stage. Saved rows reference the stable `SiteId`,
  so they survive workbook re-imports. (Monday CRM hand-off builds on the `Transferred` stage next.)
- **Methodology page:** the algorithm, background on EV Edison / Power Edison, and the workbook's
  source notes.

## Scoring engine

`NJTruckCharging.Domain/Scoring/SiteScorer.cs` implements the algorithm. All weights and thresholds
are in `ScoringRules.cs`. It reproduces the workbook exactly: every one of the 30,584 scores is within
±0.05 (rounding), and every rank in every segment is identical. Each import checks this again and logs
a warning if it drifts.

| Category | Max | Rule |
|---|---|---|
| Distance to eligible exit | 20 | 20 within 0.5 mi, then linear down to 0 at 5 mi |
| Open land | 20 | ≥3 ac 20 · 1.5–3 ac 16 · 1–1.5 ac 10 · 0.6–1 ac 3 · <0.6 ac −15 |
| Property class | 15 | Industrial 15 · Vacant 11 · Commercial 8 (best class among a merged site's lots) |
| Trucking on site | 10 | freight business 10 · other industrial 4 |
| Truck demand | 35 | fleets ≤5 mi 12 · truck AADT 10 · large warehouses 6 (each a **percentile within the segment**) · truck stop ≤2 mi 3 · port ≤5 mi 4 / ≤10 mi 2 |
| No truck charger nearby | 10 | 10 if the nearest MHD charger is >10 mi away, otherwise 4 |
| No fast charger nearby | 5 | nearest DCFC >2 mi |
| Lot shape | 5 | compactness ≥0.5 and short side ≥180 ft → 5 · short side <120 ft → −10 |
| Penalties | | far from a truck road −10 · Highlands Preservation −15 · busy multi-tenant −5 · flood −10 |

The workbook also follows three rules that its rules table doesn't state:

1. **Flood:** only the top 150 sites per segment were checked against FEMA maps. Every unchecked site
   gets the −10 flood penalty, so it stays ranked below the verified shortlist. You can turn this off
   with `ScoringRules.PenalizeUncheckedFlood`.
2. **Busy multi-tenant:** the workbook lists at most 3 business names, not how many businesses there
   are. The importer recovers the flag (3,560 sites) by comparing the computed score with the
   workbook's score.
3. **Ties:** ranks go by score rounded to one decimal, then by distance to the exit, closest first.

## API

| Method | Route | |
|---|---|---|
| GET | `/api/sites/map?…filter` | compact points for the map |
| GET | `/api/sites?…filter&page&pageSize&sortBy&descending` | ranked, paged list |
| GET | `/api/sites/{id}` | site detail, score breakdown and nearby fleets |
| GET | `/api/sites/export?…filter` | CSV download |
| GET | `/api/dashboard/summary?…filter` | KPIs, per-segment stats and distributions |
| GET | `/api/power-edison-sites`, `/api/segments`, `/api/methodology`, `/api/scoring-rules`, `/api/filter-options`, `/api/interchanges` | reference data |
| GET/POST/PUT/DELETE | `/api/saved-sites` (+ `/refs`, `/summary`) | saved sites and pipeline status |
| POST | `/api/admin/import` (multipart `file`) | replace all data with a new workbook |
| POST | `/api/admin/rescore` | re-run the scoring engine |

Filter parameters: `segments` (repeat for several), `minScore`, `topN`, `propertyClasses`
(industrial | vacant | commercial), `minOpenAcres`, `maxMilesToExit`, `freightOnSite`,
`excludeFloodZone`, `onlyFloodChecked`, `excludeHighlandsPreservation`, `excludeContaminated`,
`excludeNearPowerEdison`, `excludeBusyMultiTenant`, `utility`, `county`, `highway`, `search`.

## Access & deployment security

The app has **no login** — it opens straight to the dashboard for fast C3 site discovery. Because
there is no per-user gate, protect it at the deployment layer:

- **Keep the API off the public internet.** Bind Kestrel to localhost (the dev profile already uses
  `http://localhost:5172`) or place it behind a reverse proxy / VPN with network access control. Set
  the dashboard origin(s) via `Cors:Origins`; CORS is restricted to those origins and to GET/POST/PUT/DELETE.
- **Destructive admin endpoints** (`/api/admin/import`, `/api/admin/rescore`) are **allowed in
  Development** but **disabled in any other environment** unless an admin key is configured and sent:
  set `Admin:ApiKey` (user-secrets / env var) and pass it in the `X-Admin-Key` header. Without it they
  return `403`. Everything else (discovery, map, scoring, methodology, saved bookmarks, evaluation)
  stays open.

## Migrations

```powershell
cd NJTruckCharging
dotnet ef migrations add <Name> --project NJTruckCharging.Infrastructure --startup-project NJTruckCharging --output-dir Persistence/Migrations
```

The API applies pending migrations when it starts.
