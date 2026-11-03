/* =====================================================================================================
   C3 Site Finder — remove test fixtures from the dev database      APPROVED + EXECUTED 2026-11-02
   -----------------------------------------------------------------------------------------------------
   Requires explicit approval. See docs/maintenance/2026-11-02-test-fixture-cleanup.md for the evidence.

   Removes: 10 FeasibilityItems (Id 12–21) and 7 SiteContacts (Id 7–13), all written by test harnesses
   on 2026-11-01 (njtc-verify/phaseE.mjs and inline curl seeding), none edited since.
   Keeps:   all 30,584 CandidateSites (scores, rankings), the 3 SavedSites bookmarks (Id 13, 14, 15),
            screening decisions, and everything else.
   Also (approved): clear the test notes "E2E notes for …" on bookmarks 13 and 14 — the bookmarks are kept.

   Safety: each row is matched by Id AND SiteId AND its exact seeded content, so a row that was changed
   after the review is NOT touched. Everything runs in one transaction that rolls back unless every
   count matches exactly.

   Backup taken first: backups\NJTruckCharging_20261102_pre-test-data-cleanup.bak (CHECKSUM verified).
   Run:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d NJTruckCharging -b -i docs\maintenance\2026-11-02-test-fixture-cleanup.sql
   ===================================================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @sitesBefore int = (SELECT COUNT(*) FROM CandidateSites);
DECLARE @scoreCkBefore int = (SELECT CHECKSUM_AGG(CHECKSUM(SiteId, Score, ComputedRank, Segment)) FROM CandidateSites);
DECLARE @bookmarksBefore int = (SELECT COUNT(*) FROM SavedSites);
DECLARE @n int;

BEGIN TRANSACTION;

/* 1. Feasibility items (10) — exact seeded content. */
DELETE FROM FeasibilityItems WHERE
     (Id = 12 AND SiteId = '2004_749_1'  AND ItemKey = 'TruckEntranceAccess'   AND Status = 'Verified' AND Evidence = 'Two 40ft curb cuts on the frontage road (aerial + street view).')
  OR (Id = 13 AND SiteId = '2004_749_1'  AND ItemKey = 'EligibleHighwayAccess' AND Status = 'Verified' AND Evidence = 'Direct ramp to the interstate within 0.6 mi.')
  OR (Id = 14 AND SiteId = '2004_749_1'  AND ItemKey = 'ElectricalService'     AND Status = 'Concern'  AND Evidence = 'No 3-phase visible at the road; utility capacity study needed.')
  OR (Id = 15 AND SiteId = '1201_2706_2' AND ItemKey = 'TruckEntranceAccess'   AND Status = 'Verified' AND Evidence = 'Curb cuts confirmed on aerial + street view.')
  OR (Id = 16 AND SiteId = '1201_2706_2' AND ItemKey = 'EligibleHighwayAccess' AND Status = 'Verified' AND Evidence = 'Direct ramp to the interstate within 0.6 mi.')
  OR (Id = 17 AND SiteId = '1201_2706_2' AND ItemKey = 'ElectricalService'     AND Status = 'Concern'  AND Evidence = 'No 3-phase visible; utility capacity study needed.')
  OR (Id = 18 AND SiteId = '1412_2602_6' AND ItemKey = 'TruckEntranceAccess'   AND Status = 'Verified' AND Evidence = 'Curb cuts confirmed on aerial + street view.')
  OR (Id = 19 AND SiteId = '1412_2602_6' AND ItemKey = 'EligibleHighwayAccess' AND Status = 'Verified' AND Evidence = 'Direct ramp to the interstate within 0.6 mi.')
  OR (Id = 20 AND SiteId = '1412_2602_6' AND ItemKey = 'ElectricalService'     AND Status = 'Concern'  AND Evidence = 'No 3-phase visible; utility capacity study needed.')
  OR (Id = 21 AND SiteId = '2004_749_1'  AND ItemKey = 'TruckParkingCapacity'  AND Status = 'Verified' AND Evidence = '~48 marked trailer stalls counted on the paved yard.');
SET @n = @@ROWCOUNT;
IF @n <> 10 BEGIN ROLLBACK; RAISERROR('Expected 10 feasibility rows, matched %d — nothing deleted.', 16, 1, @n); RETURN; END

/* 2. Contacts (7) — exact seeded content. */
DELETE FROM SiteContacts WHERE
     (Id = 7  AND SiteId = '2004_749_1'  AND Name = 'Owner of 2004_749_1'  AND Email = 'owner@acme.example' AND BusinessPhone = '908-555-0100')
  OR (Id = 8  AND SiteId = '2004_749_1'  AND Name = 'Broker for 2004_749_1'  AND ResearchNotes = 'Listing agent, needs verification.')
  OR (Id = 9  AND SiteId = '1201_2706_2' AND Name = 'Owner of 1201_2706_2' AND Email = 'owner@acme.example' AND BusinessPhone = '908-555-0100')
  OR (Id = 10 AND SiteId = '1201_2706_2' AND Name = 'Broker for 1201_2706_2' AND ResearchNotes = 'Listing agent, needs verification.')
  OR (Id = 11 AND SiteId = '1412_2602_6' AND Name = 'Owner of 1412_2602_6' AND Email = 'owner@acme.example' AND BusinessPhone = '908-555-0100')
  OR (Id = 12 AND SiteId = '1412_2602_6' AND Name = 'Broker for 1412_2602_6' AND ResearchNotes = 'Listing agent, needs verification.')
  OR (Id = 13 AND SiteId = '2004_749_1'  AND Name = 'Dana Rivers'          AND SourceUrl = 'https://bayway.example');
SET @n = @@ROWCOUNT;
IF @n <> 7 BEGIN ROLLBACK; RAISERROR('Expected 7 contact rows, matched %d — nothing deleted.', 16, 1, @n); RETURN; END

/* 3. Clear test notes on two bookmarks (bookmarks are kept) — APPROVED 2026-11-02, exact text only. */
UPDATE SavedSites SET Notes = NULL, UpdatedAtUtc = SYSDATETIMEOFFSET()
 WHERE (Id = 13 AND SiteId = '1201_2706_2' AND Notes = 'E2E notes for 1201_2706_2')
    OR (Id = 14 AND SiteId = '1412_2602_6' AND Notes = 'E2E notes for 1412_2602_6');
SET @n = @@ROWCOUNT;
IF @n <> 2 BEGIN ROLLBACK; RAISERROR('Expected 2 bookmark notes, matched %d — nothing changed.', 16, 1, @n); RETURN; END

/* 4. Guards: C3 data and bookmarks untouched. */
IF (SELECT COUNT(*) FROM CandidateSites) <> @sitesBefore
   OR (SELECT CHECKSUM_AGG(CHECKSUM(SiteId, Score, ComputedRank, Segment)) FROM CandidateSites) <> @scoreCkBefore
   OR (SELECT COUNT(*) FROM SavedSites) <> @bookmarksBefore
BEGIN ROLLBACK; RAISERROR('C3 data or bookmarks changed unexpectedly — rolled back.', 16, 1); RETURN; END

COMMIT;
SELECT 'cleanup committed' AS result,
       (SELECT COUNT(*) FROM FeasibilityItems) AS feasibilityLeft,
       (SELECT COUNT(*) FROM SiteContacts)     AS contactsLeft,
       (SELECT COUNT(*) FROM SavedSites)       AS bookmarks,
       (SELECT COUNT(*) FROM CandidateSites)   AS candidateSites;
