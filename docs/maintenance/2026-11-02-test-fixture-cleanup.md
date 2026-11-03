# Test-fixture cleanup — review document (prepared and executed 2026-11-02)

**Status: APPROVED and EXECUTED on the live dev database (2026-11-02).** The SQL is in
[`2026-11-02-test-fixture-cleanup.sql`](2026-11-02-test-fixture-cleanup.sql).

## Execution log

Approval (verbatim scope): delete feasibility 12–21 and contacts 7–13 *if they still match the manifest exactly*;
keep the three bookmarks but clear the two "E2E notes" *only if the text matches exactly*.

1. Backup re-verified: `RESTORE VERIFYONLY … WITH CHECKSUM` → valid (taken 22:35:29, 80.3 MB, checksums on).
2. Live vs backup, every column of every row (restored to a scratch DB, compared with `EXCEPT` both ways):
   FeasibilityItems, SiteContacts, SavedSites, SiteScreenings and CandidateSites scoring columns — **0 differing
   rows**, i.e. the live records still matched the manifest exactly. Scratch DB dropped.
3. Section 3 (bookmark notes) enabled per approval, with its exact-text guard.
4. Script run in one transaction → `cleanup committed` (sqlcmd exit 0): 10 feasibility rows and 7 contacts deleted,
   2 bookmark notes cleared; all guards passed.
5. After: 30,584 candidates with scores/ranks byte-identical to the original baseline; 3 bookmarks (Id 13, 14, 15)
   kept with notes cleared; FeasibilityItems 0; SiteContacts 0; SiteScreenings 0.
6. Site Details for 2004_749_1, 1201_2706_2 and 1412_2602_6: 0 of 8 answers "Verified"; truck spaces, access,
   power and contact "Unknown"; checklist all Unknown; contacts empty; comparison "Not reviewed"; no seeded text
   anywhere (34/34 checks, `njtc-verify/post-cleanup-check.mjs`). Screenshots:
   `Claude outputs/details-final-post-cleanup-*-{light,dark}.png`.
7. Backend 128/128, Angular 29/29.

Undo: restore `backups\NJTruckCharging_20261102_pre-test-data-cleanup.bak` (stop the API first; `docs/RELEASE.md` §2).

## Why

The Site Details "At a glance" panel shows research marked **Verified**. On the three top-ranked sites that
research is test data written by automated harnesses on 2026-11-01, so a business owner would see false
"Verified" answers (e.g. "48 truck-suitable spaces (verified)", "Owner of 2004_749_1 (property owner)").

## Backup

`backups\NJTruckCharging_20261102_pre-test-data-cleanup.bak` — taken before any change, `RESTORE VERIFYONLY`
with CHECKSUM passed. Restore instructions: `docs/RELEASE.md` §2.

## Records proposed for deletion (17)

Identified by **content provenance**, not by date or site: every record's exact text was traced to the script or
command that wrote it, and none has been edited since (all `UpdatedAtUtc` ≤ 2026-11-01 20:50:27 UTC).

### Feasibility items (10)

| Id | Site | Item | Status | Evidence (exact) | Reviewer | Source |
|---|---|---|---|---|---|---|
| 12 | 2004_749_1 | TruckEntranceAccess | Verified | Two 40ft curb cuts on the frontage road (aerial + street view). | — | seeded by `phaseE.mjs`, overwritten by inline curl 20:50:19 UTC |
| 13 | 2004_749_1 | EligibleHighwayAccess | Verified | Direct ramp to the interstate within 0.6 mi. | dev-admin seed account | `phaseE.mjs` seed |
| 14 | 2004_749_1 | ElectricalService | Concern | No 3-phase visible at the road; utility capacity study needed. | — | seeded by `phaseE.mjs`, overwritten by inline curl 20:50:19 UTC |
| 15 | 1201_2706_2 | TruckEntranceAccess | Verified | Curb cuts confirmed on aerial + street view. | dev-admin seed account | `phaseE.mjs` seed |
| 16 | 1201_2706_2 | EligibleHighwayAccess | Verified | Direct ramp to the interstate within 0.6 mi. | dev-admin seed account | `phaseE.mjs` seed |
| 17 | 1201_2706_2 | ElectricalService | Concern | No 3-phase visible; utility capacity study needed. | dev-admin seed account | `phaseE.mjs` seed |
| 18 | 1412_2602_6 | TruckEntranceAccess | Verified | Curb cuts confirmed on aerial + street view. | dev-admin seed account | `phaseE.mjs` seed |
| 19 | 1412_2602_6 | EligibleHighwayAccess | Verified | Direct ramp to the interstate within 0.6 mi. | dev-admin seed account | `phaseE.mjs` seed |
| 20 | 1412_2602_6 | ElectricalService | Concern | No 3-phase visible; utility capacity study needed. | dev-admin seed account | `phaseE.mjs` seed |
| 21 | 2004_749_1 | TruckParkingCapacity | Verified | ~48 marked trailer stalls counted on the paved yard. | — | inline curl 20:50:19 UTC (previous session) |

### Contacts (7)

| Id | Site | Name | Org | Type | Phone / email / source | Status | Notes | Source |
|---|---|---|---|---|---|---|---|---|
| 7 | 2004_749_1 | Owner of 2004_749_1 | Acme Properties | PropertyOwner | 908-555-0100 · owner@acme.example · acme.example | Verified | Confirmed by phone. | `phaseE.mjs` seed |
| 8 | 2004_749_1 | Broker for 2004_749_1 | — | Broker | — | Unverified | Listing agent, needs verification. | `phaseE.mjs` seed |
| 9 | 1201_2706_2 | Owner of 1201_2706_2 | Acme Properties | PropertyOwner | 908-555-0100 · owner@acme.example · acme.example | Verified | Confirmed by phone. | `phaseE.mjs` seed |
| 10 | 1201_2706_2 | Broker for 1201_2706_2 | — | Broker | — | Unverified | Listing agent, needs verification. | `phaseE.mjs` seed |
| 11 | 1412_2602_6 | Owner of 1412_2602_6 | Acme Properties | PropertyOwner | 908-555-0100 · owner@acme.example · acme.example | Verified | Confirmed by phone. | `phaseE.mjs` seed |
| 12 | 1412_2602_6 | Broker for 1412_2602_6 | — | Broker | — | Unverified | Listing agent, needs verification. | `phaseE.mjs` seed |
| 13 | 2004_749_1 | Dana Rivers | Bayway Logistics | FacilityManager | bayway.example | Verified | — | inline curl 20:50:19 UTC (previous session); same values in `verify-contacts.mjs` |

### Corroborating test markers

- `.example` domains are reserved by RFC 2606 and can never be real websites.
- `555-0100`–`0199` is the range reserved for fictional telephone numbers.
- "Acme Properties" and one phone/email shared by three different "owners"; placeholder names "Owner of <SiteId>".
- Reviewer/creator `admin@poweredison.com` is the development admin account seeded in Phase A and removed when login
  was dropped — not a person's review.
- The 20:50 records were written by the previous Claude Code session while validating business filters
  (commit `f9b08e1`), by these commands (from that session's transcript):
  `PUT /api/feasibility {"siteId":"2004_749_1","itemKey":"TruckParkingCapacity","status":"Verified","evidence":"~48 marked trailer stalls counted on the paved yard."}`,
  the matching TruckEntranceAccess / ElectricalService PUTs, and
  `POST /api/contacts {"name":"Dana Rivers","organization":"Bayway Logistics",…,"sourceUrl":"https://bayway.example"}`.

## Preserved (not touched)

| What | Count | Note |
|---|---|---|
| C3 candidate sites, scores, rankings | 30,584 | guarded by a checksum inside the transaction |
| Bookmarks (SavedSites) | 3 (Id 13, 14, 15) | **kept** |
| Screening decisions | 0 | none exist |
| Real research | — | none found in FeasibilityItems or SiteContacts — every row is a fixture |

**Optional, separate approval:** bookmarks 13 and 14 carry test notes `E2E notes for 1201_2706_2` /
`E2E notes for 1412_2602_6` (from `phaseE.mjs`). Section 3 of the SQL (commented out) would clear those two notes
while keeping the bookmarks.

## Rehearsal (2026-11-02)

Run against a scratch database restored from the backup (`NJTruckCharging_CleanupRehearsal`), never the live DB:

- Run 1: committed — 10 feasibility + 7 contacts removed; 30,584 sites with an identical score/rank checksum;
  3 bookmarks intact.
- Run 2: refused — "Expected 10 feasibility rows, matched 0 — nothing deleted." (exit code 1, rolled back).

## To approve

Reply with: *"Approve cleanup of feasibility 12–21 and contacts 7–13"* (and, if wanted, *"also clear the two
bookmark notes"*). Then:

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d NJTruckCharging -b -i docs\maintenance\2026-11-02-test-fixture-cleanup.sql
```

To undo: restore `backups\NJTruckCharging_20261102_pre-test-data-cleanup.bak` (stop the API first; `docs/RELEASE.md` §2).
