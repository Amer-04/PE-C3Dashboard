using System.Globalization;
using System.Text;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Common;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

public sealed class SiteQueryService(IReadStore store, Methodology.ActiveMethodologyCache methodology, ICustomFieldStore customFields)
{
    public Task<List<SiteMapPointDto>> GetMapPointsAsync(SiteFilter filter, CancellationToken ct = default) =>
        store.ToListAsync(filter.Apply(store.Sites, store.FeasibilityItems, store.SiteContacts, store.SiteScreenings).Select(s => new SiteMapPointDto(
            s.Id, s.Latitude, s.Longitude, s.Score, s.Segment, s.ComputedRank,
            s.OpenAcresEst, s.FreightUse, s.InFloodZone, s.NearPowerEdisonSite)), ct);

    public async Task<PagedResult<SiteListItemDto>> GetPageAsync(SiteFilter filter, SiteListQuery query, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(1, query.Page);
        var q = filter.Apply(store.Sites, store.FeasibilityItems, store.SiteContacts, store.SiteScreenings);
        var total = await store.CountAsync(q, ct);
        var items = await store.ToListAsync(
            Sort(q, query).Skip((page - 1) * pageSize).Take(pageSize).Select(ToListItem), ct);
        items = await ApplyIosReviewsAsync(items, ct);
        items = await ApplyScreeningAsync(items, ct);
        items = await ApplyResearchAsync(items, ct);
        return new PagedResult<SiteListItemDto>(items, page, pageSize, total);
    }

    public async Task<SiteDetailDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        var s = await store.FirstOrDefaultAsync(store.Sites.Where(x => x.Id == id), ct);
        if (s is null) return null;

        var inSegment = await store.CountAsync(store.Sites.Where(x => x.Segment == s.Segment), ct);
        var fleets = await store.ToListAsync(store.NearbyFleets
            .Where(f => f.CandidateSiteId == id)
            .OrderByDescending(f => f.Trucks)
            .Select(f => new NearbyFleetDto(f.Carrier, f.Dba, f.Trucks, f.Tractors, f.Drivers, f.CarrierAddress,
                f.ApproxMiles, f.Phone, f.Email, f.UsDot, f.Operation, f.Cargo)), ct);

        // Site-evaluation research for this site (feasibility verdicts + contacts), joined by stable SiteId.
        var feas = await store.ToListAsync(store.FeasibilityItems.Where(f => f.SiteId == s.SiteId), ct);
        FeasibilityItem? Item(FeasibilityItemKey k) => feas.FirstOrDefault(f => f.ItemKey == k);
        var entrance = Item(FeasibilityItemKey.TruckEntranceAccess);
        var electrical = Item(FeasibilityItemKey.ElectricalService);
        var parking = Item(FeasibilityItemKey.TruckParkingCapacity);
        var ios = IosRules.Assess(s, Item(IosRules.ReviewKey));
        var screening = await store.FirstOrDefaultAsync(store.SiteScreenings.Where(x => x.SiteId == s.SiteId), ct);
        var contacts = await store.ToListAsync(store.SiteContacts.Where(c => c.SiteId == s.SiteId), ct);
        var verifiedContacts = contacts.Count(c => c.VerificationStatus == ContactVerificationStatus.Verified);
        var totalContacts = contacts.Count;
        var feasByKey = feas.GroupBy(f => f.ItemKey).ToDictionary(g => g.Key, g => g.First());
        var suitability = SiteSuitability.Build(s, feasByKey, contacts);
        var validation = SiteValidation.Build(s, feasByKey, contacts, screening?.Decision ?? ScreeningDecision.NotReviewed, suitability);
        // Per-criterion points of the version whose score this row carries (the active one).
        var scoreRow = s.ScoreVersionId is { } sv
            ? await store.FirstOrDefaultAsync(store.SiteVersionScores.Where(x => x.MethodologyVersionId == sv && x.SiteId == s.SiteId), ct)
            : null;

        return new SiteDetailDto
        {
            Suitability = suitability,
            Validation = validation,
            IosEvidence = ios.Evidence,
            IosDerivedClassification = ios.DerivedClassification,
            IosDerivedEvidence = ios.DerivedEvidence,
            IosReviewStatus = ios.ReviewStatus,
            TruckEntranceStatus = entrance?.Status ?? FeasibilityStatus.Unknown,
            TruckEntranceEvidence = entrance?.Evidence,
            ElectricalStatus = electrical?.Status ?? FeasibilityStatus.Unknown,
            ElectricalEvidence = electrical?.Evidence,
            ParkingStatus = parking?.Status ?? FeasibilityStatus.Unknown,
            VerifiedParkingEvidence = parking is { Status: FeasibilityStatus.Verified } ? parking.Evidence : null,
            VerifiedContactCount = verifiedContacts,
            TotalContactCount = totalContacts,
            Summary = ToListItemCompiled(s) with
            {
                IosClassification = ios.Classification, IosBasis = ios.Basis,
                ScreeningDecision = screening?.Decision ?? ScreeningDecision.NotReviewed,
                SiteEvidence = validation.SiteEvidence, ContactReadiness = validation.ContactReadiness,
                TopContact = ResearchReadiness.TopContact(contacts),
                BusinessListingConfirmed = BusinessConfirmed(feas)
            },
            ScreeningEvidence = screening?.Evidence,
            ScreeningUpdatedAtUtc = screening?.UpdatedAtUtc,
            ScoreLines = ScoreLines.For(s, inSegment, methodology.Current, scoreRow,
                await Methodology.CustomCatalog.BuildAsync(customFields, s.SiteId, ct)),
            TruckDemandPoints = Math.Round(s.TruckDemandPoints, 1),
            MethodologyVersionId = s.ScoreVersionId,
            MethodologyVersionName = methodology.Current is { } am && am.VersionId == s.ScoreVersionId ? am.Name : null,
            SitesInSegment = inSegment,
            PamsPin = s.PamsPin, Block = s.Block, Lot = s.Lot, Qual = s.Qual, LotsInSite = s.LotsInSite, MemberLots = s.MemberLots,
            RecordedAcres = s.RecordedAcres, BuildingCoverage = s.BuildingCoverage, Zoning = s.Zoning,
            ShortSideFt = s.ShortSideFt, Compactness = s.Compactness,
            BuildingDescription = s.BuildingDescription, LandDescription = s.LandDescription, YearBuilt = s.YearBuilt,
            LandValue = s.LandValue, ImprovementValue = s.ImprovementValue, AnnualTax = s.AnnualTax,
            LastSalePrice = s.LastSalePrice, LastDeedDate = s.LastDeedDate,
            OwnerName = s.OwnerName, OwnerMailAddress = s.OwnerMailAddress, OwnerMailCity = s.OwnerMailCity,
            OwnerMailZip = s.OwnerMailZip, OwnerOccupiedHint = s.OwnerOccupiedHint,
            BusinessCategories = s.BusinessCategories, BusinessPhone = s.BusinessPhone, BusinessWebsite = s.BusinessWebsite,
            IndustrialUse = s.IndustrialUse, BusyMultiTenant = s.BusyMultiTenant,
            DemandSummary = s.DemandSummary, TrucksBased3Mi = s.TrucksBased3Mi, TrucksBased10Mi = s.TrucksBased10Mi,
            CarriersBased5Mi = s.CarriersBased5Mi, TractorsBased5Mi = s.TractorsBased5Mi,
            TruckAadtAdjacent = s.TruckAadtAdjacent, TruckAadtAtExit = s.TruckAadtAtExit,
            MilesToTruckRoad = s.MilesToTruckRoad, TruckStops2Mi = s.TruckStops2Mi, MilesToTruckStop = s.MilesToTruckStop,
            MilesToPort = s.MilesToPort, NearestPort = s.NearestPort, ExitName = s.ExitName,
            MilesToTruckDcfc = s.MilesToTruckDcfc, MilesToAnyDcfc = s.MilesToAnyDcfc, NearestDcfc = s.NearestDcfc,
            Highlands = s.Highlands, FloodZone100Yr = s.FloodZone100Yr, KnownContaminatedSites = s.KnownContaminatedSites,
            NearbyFleets = fleets
        };
    }

    /// <summary>Overlays the team's screening decisions on list rows (joined by stable SiteId).</summary>
    private async Task<List<SiteListItemDto>> ApplyScreeningAsync(List<SiteListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return items;
        var ids = items.Select(i => i.SiteId).ToList();
        var decisions = (await store.ToListAsync(store.SiteScreenings.Where(x => ids.Contains(x.SiteId)), ct))
            .ToDictionary(x => x.SiteId, x => x.Decision);
        return decisions.Count == 0 ? items
            : items.Select(i => decisions.TryGetValue(i.SiteId, out var d) ? i with { ScreeningDecision = d } : i).ToList();
    }

    /// <summary>Overlays Site Evidence and Contact Readiness (research records only, joined by stable SiteId).</summary>
    private async Task<List<SiteListItemDto>> ApplyResearchAsync(List<SiteListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return items;
        var ids = items.Select(i => i.SiteId).ToList();
        var feas = (await store.ToListAsync(store.FeasibilityItems.Where(f => ids.Contains(f.SiteId)), ct)).ToLookup(f => f.SiteId);
        var contacts = (await store.ToListAsync(store.SiteContacts.Where(c => ids.Contains(c.SiteId)), ct)).ToLookup(c => c.SiteId);
        return items.Select(i => i with
        {
            SiteEvidence = ResearchReadiness.Evidence(feas[i.SiteId], i.ScreeningDecision),
            ContactReadiness = ResearchReadiness.Readiness(contacts[i.SiteId]),
            TopContact = ResearchReadiness.TopContact(contacts[i.SiteId].ToList()),
            BusinessListingConfirmed = BusinessConfirmed(feas[i.SiteId])
        }).ToList();
    }

    /// <summary>A reviewer verified, with evidence, that the listed business operates at the address.</summary>
    private static bool BusinessConfirmed(IEnumerable<FeasibilityItem> siteItems) =>
        siteItems.Any(f => f.ItemKey == ResearchReadiness.BusinessKey && f.Status == FeasibilityStatus.Verified && !string.IsNullOrEmpty(f.Evidence));

    private static string? OwnerMailing(CandidateSite s)
    {
        var line = string.Join(", ", new[] { s.OwnerMailAddress, s.OwnerMailCity }.Where(x => !string.IsNullOrWhiteSpace(x)));
        line = string.IsNullOrWhiteSpace(s.OwnerMailZip) ? line : $"{line} {s.OwnerMailZip}".Trim();
        return line.Length == 0 ? null : line;
    }

    /// <summary>Overlays manual outdoor-storage verdicts (IosRules.Resolve) on source-derived list rows.</summary>
    private async Task<List<SiteListItemDto>> ApplyIosReviewsAsync(List<SiteListItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return items;
        var ids = items.Select(i => i.SiteId).ToList();
        var reviews = (await store.ToListAsync(store.FeasibilityItems
                .Where(f => f.ItemKey == IosRules.ReviewKey && ids.Contains(f.SiteId)), ct))
            .ToDictionary(f => f.SiteId, f => f.Status);
        if (reviews.Count == 0) return items;
        return items.Select(i =>
        {
            if (!reviews.TryGetValue(i.SiteId, out var status)) return i;
            var (cls, basis) = IosRules.Resolve(i.IosClassification, status);
            return i with { IosClassification = cls, IosBasis = basis };
        }).ToList();
    }

    /// <summary>CSV of the filtered sites (ranked), for desktop review and outreach.</summary>
    public async Task<byte[]> ExportCsvAsync(SiteFilter filter, CancellationToken ct = default)
    {
        var rows = await store.ToListAsync(filter.Apply(store.Sites, store.FeasibilityItems, store.SiteContacts, store.SiteScreenings)
            .OrderBy(s => s.Segment).ThenBy(s => s.ComputedRank), ct);
        var iosReviews = (await store.ToListAsync(store.FeasibilityItems.Where(f => f.ItemKey == IosRules.ReviewKey), ct))
            .ToDictionary(f => f.SiteId);
        var screenings = (await store.ToListAsync(store.SiteScreenings, ct)).ToDictionary(x => x.SiteId);
        var versions = await VersionNamesAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("segment,rank,score,site_id,address,municipality,county,prop_class,acres,open_acres," +
                      "est_parking_preliminary,ios_classification,ios_basis,miles_to_exit,highway,exit," +
                      "trucks_5mi,truck_traffic,warehouses_2mi,businesses,freight_use,utility,flood_zone,contaminated,highlands," +
                      "nearest_pe_site,miles_to_pe_site,owner_mail_address,owner_mail_city,owner_mail_zip,business_phone,lat,lon," +
                      "screening_decision,screening_evidence,methodology_version");
        foreach (var s in rows)
        {
            var ios = IosRules.Assess(s, iosReviews.GetValueOrDefault(s.SiteId));
            var sc = screenings.GetValueOrDefault(s.SiteId);
            string[] cells =
            [
                I(s.Segment), I(s.ComputedRank), F(s.Score), s.SiteId, s.SiteAddress, s.Municipality, s.County, s.PropClass,
                F(s.Acres), F(s.OpenAcresEst),
                s.OpenAcresEst > 0 ? I((int)(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre)) : "unknown",
                IosToken(ios.Classification), IosBasisText(ios.Basis),
                F(s.MilesToExit), s.NearestHighway ?? "", s.NearestExit ?? "",
                I(s.TrucksBased5Mi), I(Math.Max(s.TruckAadtAdjacent, s.TruckAadtAtExit)), I(s.LargeWarehouses2Mi),
                s.BusinessesOnSite ?? "", s.FreightUse ? "yes" : "no", s.Utility ?? "", s.FloodZone100Yr ?? "not checked",
                s.KnownContaminatedSites?.ToString(CultureInfo.InvariantCulture) ?? "not checked", s.Highlands ?? "",
                s.NearestPowerEdisonSite ?? "", F(s.MilesToPowerEdisonSite), s.OwnerMailAddress ?? "", s.OwnerMailCity ?? "",
                s.OwnerMailZip ?? "", s.BusinessPhone ?? "", F(s.Latitude, "0.######"), F(s.Longitude, "0.######"),
                Screening.ScreeningRules.Label(sc?.Decision ?? ScreeningDecision.NotReviewed), sc?.Evidence ?? "",
                VersionLabel(versions, s.ScoreVersionId)
            ];
            sb.AppendLine(string.Join(',', cells.Select(Csv)));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);
        static string Csv(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    /// <summary>
    /// Research shortlist CSV: one row per filtered site with the parcel evidence, highway, preliminary vs verified
    /// parking, truck access, power, the best professional contact, evidence source URLs and what is still missing.
    /// Unknown stays "Unknown" — nothing is filled in with estimates (the preliminary parking column is labelled as such).
    /// </summary>
    public async Task<byte[]> ExportResearchCsvAsync(SiteFilter filter, CancellationToken ct = default)
    {
        var rows = await store.ToListAsync(filter.Apply(store.Sites, store.FeasibilityItems, store.SiteContacts, store.SiteScreenings)
            .OrderBy(s => s.Segment).ThenBy(s => s.ComputedRank), ct);
        var feas = (await store.ToListAsync(store.FeasibilityItems, ct)).ToLookup(f => f.SiteId);
        var contacts = (await store.ToListAsync(store.SiteContacts, ct)).ToLookup(c => c.SiteId);
        var screenings = (await store.ToListAsync(store.SiteScreenings, ct)).ToDictionary(x => x.SiteId);
        var versions = await VersionNamesAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', ResearchColumns));
        foreach (var s in rows)
        {
            var items = feas[s.SiteId].GroupBy(f => f.ItemKey).ToDictionary(g => g.Key, g => g.First());
            var cs = contacts[s.SiteId].ToList();
            var sc = screenings.GetValueOrDefault(s.SiteId);
            var decision = sc?.Decision ?? ScreeningDecision.NotReviewed;
            var suit = SiteSuitability.Build(s, items, cs);
            var v = SiteValidation.Build(s, items, cs, decision, suit);
            FeasibilityItem? Item(FeasibilityItemKey k) => items.GetValueOrDefault(k);
            FeasibilityItem? Reviewed(FeasibilityItemKey k) => Item(k) is { Status: not FeasibilityStatus.Unknown } f ? f : null;
            string St(FeasibilityItemKey k) => (Item(k)?.Status ?? FeasibilityStatus.Unknown).ToString();
            string Ev(FeasibilityItemKey k) => Reviewed(k)?.Evidence ?? "";
            string Checked(FeasibilityItemKey k) => Reviewed(k) is { } f ? D(f.ReviewedAtUtc ?? f.UpdatedAtUtc) : "";
            var parking = Item(FeasibilityItemKey.TruckParkingCapacity);
            var verifiedCount = parking is { Status: FeasibilityStatus.Verified } ? SiteSuitability.ParseSpaceCount(parking.Evidence) : null;
            var best = ResearchReadiness.BestContact(cs);
            var others = cs.Where(c => c != best && c.VerificationStatus != ContactVerificationStatus.Invalid).ToList();
            var urls = v.Sections.SelectMany(x => x.EvidenceLinks)
                .Concat(cs.Where(c => c.VerificationStatus != ContactVerificationStatus.Invalid && ResearchReadiness.IsSourced(c)).Select(c => c.SourceUrl!))
                .Concat(SiteValidation.Links(sc?.Evidence))
                .Distinct().ToList();
            var parcel = v.Sections.Single(x => x.Key == "Parcel");

            string[] cells =
            [
                s.SiteId, s.SiteAddress, s.Municipality, s.County, I(s.Segment), I(s.ComputedRank), F2(s.Score),
                Screening.ScreeningRules.Label(decision), sc?.Evidence ?? "",
                ResearchReadiness.Label(v.SiteEvidence), ResearchReadiness.Label(v.ContactReadiness),
                s.Block ?? "Unknown", s.Lot ?? "Unknown", s.PamsPin ?? "Unknown",
                St(ResearchReadiness.ParcelKey), Ev(ResearchReadiness.ParcelKey), Checked(ResearchReadiness.ParcelKey),
                string.Join(" | ", parcel.Discrepancies),
                s.BusinessesOnSite ?? "", s.BusinessPhone ?? "", OwnerMailing(s) ?? "", St(ResearchReadiness.BusinessKey), Ev(ResearchReadiness.BusinessKey),
                s.NearestHighway ?? "Unknown", s.NearestExit ?? "", s.NearestHighway is null ? "Unknown" : F2(s.MilesToExit),
                St(FeasibilityItemKey.EligibleHighwayAccess), Ev(FeasibilityItemKey.EligibleHighwayAccess),
                s.OpenAcresEst > 0 ? I((int)(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre)) : "Unknown",
                St(FeasibilityItemKey.TruckParkingCapacity), verifiedCount is { } n ? I(n) : "Unknown", Ev(FeasibilityItemKey.TruckParkingCapacity),
                St(FeasibilityItemKey.TruckEntranceAccess), St(FeasibilityItemKey.TurningManeuveringSpace),
                string.Join(" | ", new[] { Ev(FeasibilityItemKey.TruckEntranceAccess), Ev(FeasibilityItemKey.TurningManeuveringSpace) }.Where(x => x.Length > 0)),
                St(FeasibilityItemKey.ElectricalService), Ev(FeasibilityItemKey.ElectricalService), s.Utility ?? "Unknown",
                best?.Name ?? "", best is null ? "" : best.ContactType.ToString(), best?.JobTitle ?? "", best?.Organization ?? "",
                best?.BusinessPhone ?? "", best?.Email ?? "",
                best is null ? "" : best.VerificationStatus == ContactVerificationStatus.Verified && ResearchReadiness.IsSourced(best) ? "Verified" : "Unverified",
                best?.SourceUrl ?? "", best is null ? "" : D(best.LastVerifiedUtc ?? best.UpdatedAtUtc),
                string.Join(" | ", others.Select(c => $"{c.Name} ({c.ContactType}, {c.VerificationStatus})")),
                string.Join(" | ", urls),
                string.Join(" | ", v.MissingVerification),
                VersionLabel(versions, s.ScoreVersionId)
            ];
            sb.AppendLine(string.Join(',', cells.Select(Csv)));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string F2(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static string D(DateTimeOffset d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static string Csv(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    public static readonly string[] ResearchColumns =
    [
        "site_id", "address", "municipality", "county", "segment", "rank", "c3_score",
        "screening_decision", "screening_evidence", "site_evidence", "contact_readiness",
        "parcel_block", "parcel_lot", "pams_pin", "parcel_check_status", "parcel_check_evidence", "parcel_checked_on", "parcel_discrepancies",
        "business_listed_in_source", "business_phone_listed_in_source", "owner_mailing_address", "business_at_address_status", "business_at_address_evidence",
        "highway", "exit", "miles_to_exit_straight_line", "highway_access_status", "highway_access_evidence",
        "parking_preliminary_estimate", "parking_verified_status", "parking_verified_count", "parking_verified_evidence",
        "truck_entrance_status", "turning_space_status", "truck_access_evidence",
        "power_status", "power_evidence", "utility_territory",
        "contact_name", "contact_role", "contact_title", "contact_organization", "contact_phone", "contact_email",
        "contact_status", "contact_source_url", "contact_last_checked", "other_contacts",
        "evidence_source_urls", "missing_verification", "methodology_version"
    ];

    /// <summary>Version names, so every exported row states which methodology produced its score/rank.</summary>
    private async Task<Dictionary<int, string>> VersionNamesAsync(CancellationToken ct) =>
        (await store.ToListAsync(store.MethodologyVersions.Select(v => new { v.Id, v.Name }), ct)).ToDictionary(v => v.Id, v => v.Name);

    private static string VersionLabel(Dictionary<int, string> names, int? id) =>
        id is { } v ? $"v{v} {names.GetValueOrDefault(v, "")}".Trim() : "";

    /// <summary>
    /// Orders the list. Every sort ends with the primary key as a tie-breaker so ties (e.g. equal score and exit
    /// distance) come back in a stable order — otherwise paging could repeat or skip sites at page boundaries.
    /// </summary>
    private static IQueryable<CandidateSite> Sort(IQueryable<CandidateSite> q, SiteListQuery query) =>
        Primary(q, query).ThenBy(s => s.Id);

    private static IOrderedQueryable<CandidateSite> Primary(IQueryable<CandidateSite> q, SiteListQuery query) => (query.SortBy, query.Descending) switch
    {
        (SiteSort.Rank, false) => q.OrderBy(s => s.ComputedRank).ThenBy(s => s.Segment),
        (SiteSort.Rank, true) => q.OrderByDescending(s => s.ComputedRank).ThenBy(s => s.Segment),
        (SiteSort.OpenAcres, true) => q.OrderByDescending(s => s.OpenAcresEst),
        (SiteSort.OpenAcres, false) => q.OrderBy(s => s.OpenAcresEst),
        (SiteSort.MilesToExit, true) => q.OrderByDescending(s => s.MilesToExit),
        (SiteSort.MilesToExit, false) => q.OrderBy(s => s.MilesToExit),
        (SiteSort.TrucksBased5Mi, true) => q.OrderByDescending(s => s.TrucksBased5Mi),
        (SiteSort.TrucksBased5Mi, false) => q.OrderBy(s => s.TrucksBased5Mi),
        (SiteSort.TruckTraffic, true) => q.OrderByDescending(s => s.TruckAadtAdjacent > s.TruckAadtAtExit ? s.TruckAadtAdjacent : s.TruckAadtAtExit),
        (SiteSort.TruckTraffic, false) => q.OrderBy(s => s.TruckAadtAdjacent > s.TruckAadtAtExit ? s.TruckAadtAdjacent : s.TruckAadtAtExit),
        (SiteSort.MilesToPowerEdison, true) => q.OrderByDescending(s => s.MilesToPowerEdisonSite),
        (SiteSort.MilesToPowerEdison, false) => q.OrderBy(s => s.MilesToPowerEdisonSite),
        (_, false) => q.OrderBy(s => s.Score).ThenByDescending(s => s.MilesToExit),
        _ => q.OrderByDescending(s => s.Score).ThenBy(s => s.MilesToExit)
    };

    /// <summary>CSV / filter token for a classification.</summary>
    public static string IosToken(IosClassification c) => c switch
    {
        IosClassification.Confirmed => "confirmed",
        IosClassification.FreightIndicated => "indicated",
        IosClassification.Potential => "potential",
        IosClassification.NotIos => "not",
        _ => "unknown"
    };

    public static string IosBasisText(IosBasis b) => b == IosBasis.ManualReview ? "manual review" : "source-derived (not verified)";

    // Source-derived IOS class comes from IosRules.Derived (computed in SQL); manual reviews are overlaid
    // afterwards via IosRules.Resolve.
    private static readonly System.Linq.Expressions.Expression<Func<CandidateSite, SiteListItemDto>> ToListItem =
        IosRules.WithDerived<SiteListItemDto>((s, ios) =>
        new SiteListItemDto(
            s.Id, s.SiteId, s.Segment, s.ComputedRank, s.Score, s.FileScore,
            s.SiteAddress, s.Municipality, s.County, s.PropClass,
            s.Acres, s.OpenAcresEst, s.MilesToExit, s.NearestHighway, s.NearestExit,
            s.TrucksBased5Mi, s.TruckAadtAdjacent > s.TruckAadtAtExit ? s.TruckAadtAdjacent : s.TruckAadtAtExit, s.LargeWarehouses2Mi,
            s.BusinessesOnSite, s.FreightUse, s.Utility,
            s.InFloodZone, s.FloodChecked, s.IsContaminated, s.InHighlandsPreservation,
            s.NearPowerEdisonSite, s.NearestPowerEdisonSite, s.MilesToPowerEdisonSite,
            s.Latitude, s.Longitude,
            // Preliminary estimate only; null (Unknown) when there is no open-acreage estimate.
            s.OpenAcresEst > 0 ? (int)(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre) : (int?)null,
            ios, IosBasis.SourceDerived,
            ScreeningDecision.NotReviewed, SiteEvidence.Unknown, ContactReadiness.NoContact, null,
            // Listed general business line from the C3 workbook (Overture places) — shown as-is, never as a contact.
            s.BusinessPhone,
            s.OwnerMailAddress, s.OwnerMailCity, s.OwnerMailZip,
            false,
            s.BusinessWebsite));

    // Declared after ToListItem: static fields initialise in textual order.
    private static readonly Func<CandidateSite, SiteListItemDto> ToListItemCompiled = ToListItem.Compile();
}
