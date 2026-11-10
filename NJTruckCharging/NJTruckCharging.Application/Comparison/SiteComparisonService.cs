using System.Globalization;
using System.Text;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Application.Feasibility;
using NJTruckCharging.Application.Screening;
using NJTruckCharging.Application.Sites;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Comparison;

/// <summary>
/// Side-by-side comparison of 2–5 C3 candidates, keyed by the stable <see cref="CandidateSite.SiteId"/> (so a
/// selection survives workbook re-imports). Read-only: never changes scores, rankings or data. Unknown or
/// unreviewed values are always shown as such — nothing is treated as verified without a reviewer's verdict.
/// Highlights differences between sites but never declares a winner.
/// </summary>
public sealed class SiteComparisonService(IReadStore store)
{
    public const int MinSites = 2;
    public const int MaxSites = 5;

    public const string ScoreNote =
        "C3 scores and ranks are relative to each site's own NOFA segment (demand percentiles are computed within the segment). " +
        "Scores from different segments are not a head-to-head ranking.";

    private static readonly FeasibilityItemKey[] AllKeys = Enum.GetValues<FeasibilityItemKey>();

    /// <summary>Normalises the requested ids (trim, de-duplicate, keep order) and validates the 2–5 rule.</summary>
    public static (List<string> Ids, string? Error) Normalise(IEnumerable<string>? siteIds)
    {
        var ids = (siteIds ?? [])
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count < MinSites) return (ids, $"Select at least {MinSites} sites to compare.");
        if (ids.Count > MaxSites) return (ids, $"You can compare at most {MaxSites} sites at a time.");
        return (ids, null);
    }

    public async Task<SiteComparisonDto> CompareAsync(IReadOnlyList<string> siteIds, CancellationToken ct = default)
    {
        var ids = siteIds.ToList();
        var sites = await store.ToListAsync(store.Sites.Where(s => ids.Contains(s.SiteId)), ct);
        var segments = sites.Select(s => s.Segment).Distinct().ToList();

        var segmentSizes = (await store.ToListAsync(store.Sites
                .Where(s => segments.Contains(s.Segment))
                .GroupBy(s => s.Segment)
                .Select(g => new { Segment = g.Key, Count = g.Count() }), ct))
            .ToDictionary(x => x.Segment, x => x.Count);
        var segmentCounties = (await store.ToListAsync(store.Segments.Where(g => segments.Contains(g.Id)), ct))
            .ToDictionary(g => g.Id, g => g.Counties);
        var feasibility = (await store.ToListAsync(store.FeasibilityItems.Where(f => ids.Contains(f.SiteId)), ct))
            .GroupBy(f => f.SiteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToDictionary(f => f.ItemKey), StringComparer.OrdinalIgnoreCase);

        var screenings = (await store.ToListAsync(store.SiteScreenings.Where(x => ids.Contains(x.SiteId)), ct))
            .ToDictionary(x => x.SiteId, StringComparer.OrdinalIgnoreCase);
        var byId = sites.ToDictionary(s => s.SiteId, StringComparer.OrdinalIgnoreCase);
        var compared = ids.Where(byId.ContainsKey)
            .Select(id => Build(byId[id],
                segmentSizes.GetValueOrDefault(byId[id].Segment),
                segmentCounties.GetValueOrDefault(byId[id].Segment) ?? "",
                feasibility.GetValueOrDefault(id) ?? [],
                screenings.GetValueOrDefault(id)))
            .ToList();
        var missing = ids.Where(id => !byId.ContainsKey(id)).ToList();

        return new SiteComparisonDto(compared, BuildRows(compared), missing,
            compared.Select(c => c.Segment).Distinct().Count() > 1, ScoreNote);
    }

    private static ComparedSiteDto Build(CandidateSite s, int sitesInSegment, string counties,
        IReadOnlyDictionary<FeasibilityItemKey, FeasibilityItem> feas, SiteScreening? screening)
    {
        VerificationDto V(FeasibilityItemKey k) => feas.TryGetValue(k, out var f)
            ? new VerificationDto(f.Status, f.Evidence, f.ReviewedAtUtc) { Documented = Research.ResearchQuality.IsDocumented(f), Missing = Research.ResearchQuality.MissingText(f) }
            : new VerificationDto(FeasibilityStatus.Unknown, null, null);

        var ios = IosRules.Assess(s, feas.GetValueOrDefault(IosRules.ReviewKey));
        var flood = s.FloodZone100Yr is null
            ? new ScreeningDto(ScreeningStatus.NotChecked, "Not screened in the source data")
            : string.Equals(s.FloodZone100Yr, "none", StringComparison.OrdinalIgnoreCase)
                ? new ScreeningDto(ScreeningStatus.Clear, "Outside FEMA 100-yr flood zone (desktop screen)")
                : new ScreeningDto(ScreeningStatus.Flagged, $"In FEMA 100-yr flood zone {s.FloodZone100Yr}");
        var contamination = s.KnownContaminatedSites switch
        {
            null => new ScreeningDto(ScreeningStatus.NotChecked, "Not screened in the source data"),
            0 => new ScreeningDto(ScreeningStatus.Clear, "No known contaminated sites (desktop screen)"),
            var n => new ScreeningDto(ScreeningStatus.Flagged, $"{n} known contaminated site(s) on/near parcel")
        };
        var highlands = s.Highlands switch
        {
            null or "" => new ScreeningDto(ScreeningStatus.Clear, "No Highlands designation in source data"),
            var h when h.Contains("Preservation", StringComparison.OrdinalIgnoreCase) => new ScreeningDto(ScreeningStatus.Flagged, h),
            var h => new ScreeningDto(ScreeningStatus.Caution, h)
        };

        var missing = AllKeys
            .Where(k => !feas.TryGetValue(k, out var f) || f.Status == FeasibilityStatus.Unknown)
            .Select(FeasibilityLabels.For)
            // A Verified verdict without evidence text, source URL and evidence date still needs its evidence.
            .Concat(AllKeys.Where(k => feas.TryGetValue(k, out var f) && f.Status == FeasibilityStatus.Verified && !Research.ResearchQuality.IsDocumented(f))
                .Select(k => $"{FeasibilityLabels.For(k)} (insufficient evidence)"))
            .ToList();
        if (flood.Status == ScreeningStatus.NotChecked) missing.Add("Flood-zone screening (source data)");
        if (contamination.Status == ScreeningStatus.NotChecked) missing.Add("Contamination screening (source data)");
        var concerns = AllKeys
            .Where(k => feas.TryGetValue(k, out var f) && f.Status is FeasibilityStatus.Concern or FeasibilityStatus.Conflicting)
            .Select(k => feas[k].Evidence is { Length: > 0 } e ? $"{FeasibilityLabels.For(k)}: {e}" : FeasibilityLabels.For(k))
            .ToList();

        return new ComparedSiteDto
        {
            SiteId = s.SiteId, CandidateId = s.Id,
            Address = s.SiteAddress, Municipality = s.Municipality, County = s.County,
            Segment = s.Segment, SegmentCounties = counties,
            Score = s.Score, SegmentRank = s.ComputedRank, SitesInSegment = sitesInSegment,
            PropClass = s.PropClass, PropertyClassLabel = PropertyClasses.Label(s.PropClass),
            TotalAcres = s.Acres,
            OpenAcresEst = s.OpenAcresEst > 0 ? s.OpenAcresEst : null,
            Ios = new ComparedIosDto(ios.Classification, ios.Basis, ios.Evidence, ios.DerivedClassification, ios.ReviewStatus),
            NearestHighway = s.NearestHighway, NearestExit = s.NearestExit, ExitName = s.ExitName,
            MilesToExit = s.MilesToExit,
            HighwayAccess = V(FeasibilityItemKey.EligibleHighwayAccess),
            TruckEntrance = V(FeasibilityItemKey.TruckEntranceAccess),
            EstimatedParkingPreliminary = s.OpenAcresEst > 0 ? (int)Math.Floor(s.OpenAcresEst * SiteFilter.EstimatedTrailersPerOpenAcre) : null,
            VerifiedParking = V(FeasibilityItemKey.TruckParkingCapacity),
            Utility = s.Utility,
            Electrical = V(FeasibilityItemKey.ElectricalService),
            Flood = flood, Contamination = contamination, Highlands = highlands,
            FloodEnvironmentalReview = V(FeasibilityItemKey.FloodEnvironmental),
            MissingVerification = missing, Concerns = concerns,
            ScreeningDecision = screening?.Decision ?? ScreeningDecision.NotReviewed,
            ScreeningEvidence = screening?.Evidence,
            Lat = s.Latitude, Lng = s.Longitude
        };
    }

    // ---- Rows: one definition used by the UI table, the "differs" highlight and the CSV export ----

    private static List<ComparisonRowDto> BuildRows(IReadOnlyList<ComparedSiteDto> sites)
    {
        var rows = new List<ComparisonRowDto>();
        void Row(string key, string group, string label, Func<ComparedSiteDto, (string Value, string Tone)> cell,
            bool comparable = true, string? hint = null)
        {
            var cells = sites.Select(cell).ToList();
            var values = cells.Select(c => c.Value).ToList();
            var differs = comparable && sites.Count > 1 &&
                          values.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            rows.Add(new ComparisonRowDto(key, group, label, hint, values, cells.Select(c => c.Tone).ToList(), comparable, differs));
        }

        Row("address", "Location", "Site address", s => ($"{s.Address}, {s.Municipality} ({s.County} County)", ""), comparable: false);
        Row("segment", "Location", "Geographic segment", s => (s.SegmentCounties.Length > 0 ? $"Segment {s.Segment} — {s.SegmentCounties}" : $"Segment {s.Segment}", ""));

        Row("screening", "Screening", "Team screening decision", s => (ScreeningRules.Label(s.ScreeningDecision), s.ScreeningDecision switch
        {
            ScreeningDecision.Promising => "clear",
            ScreeningDecision.NeedsMoreResearch => "caution",
            ScreeningDecision.Unsuitable => "flag",
            _ => "unknown"
        }), hint: "Suitability judgement recorded by the team — not a ranking by this comparison.");
        Row("screeningEvidence", "Screening", "Screening evidence", s => (s.ScreeningEvidence ?? "None recorded", s.ScreeningEvidence is null ? "unknown" : ""), comparable: false);

        Row("score", "C3 score", "C3 score (segment-relative)", s => (F(s.Score, "0.0"), ""),
            hint: "Scored within the site's own segment — not a head-to-head ranking across segments.");
        Row("rank", "C3 score", "Segment rank", s => ($"#{I(s.SegmentRank)} of {I(s.SitesInSegment)} in Segment {s.Segment}", ""));

        Row("propClass", "Property", "Property classification", s => ($"{s.PropertyClassLabel} (class {s.PropClass})", ""));
        Row("acres", "Property", "Total acreage", s => ($"{F(s.TotalAcres)} ac", ""));
        Row("openAcres", "Property", "Estimated open acreage", s => s.OpenAcresEst is { } o
            ? ($"{F(o)} ac (estimate)", "source") : ("Unknown — no estimate", "unknown"));

        Row("ios", "IOS", "IOS classification", s => (IosLabel(s.Ios.Classification), s.Ios.Basis == IosBasis.ManualReview
            ? (s.Ios.Classification == IosClassification.Confirmed ? "verified" : "concern")
            : s.Ios.Classification == IosClassification.Unknown ? "unknown" : "source"));
        Row("iosBasis", "IOS", "IOS basis", s => s.Ios.Basis == IosBasis.ManualReview
            ? ("Manual site review", s.Ios.Classification == IosClassification.Confirmed ? "verified" : "concern")
            : ("Source data — not manually verified", "source"));
        Row("iosEvidence", "IOS", "IOS evidence", s => (s.Ios.Evidence, ""), comparable: false);

        Row("highway", "Highway access", "Nearest eligible highway & exit", s => (Exit(s), s.NearestHighway is null ? "unknown" : ""));
        Row("milesToExit", "Highway access", "Distance to interchange", s => ($"{F(s.MilesToExit, "0.00")} mi", ""));
        Row("highwayAccess", "Highway access", "Highway access verification", s => Verdict(s.HighwayAccess));
        Row("truckEntrance", "Highway access", "Truck entrance status", s => Verdict(s.TruckEntrance));

        Row("estParking", "Parking", "Preliminary parking estimate", s => s.EstimatedParkingPreliminary is { } p
            ? ($"~{I(p)} trailers (preliminary, not verified)", "source") : ("Unknown — no open-acreage estimate", "unknown"),
            hint: "Desktop screen at ~15 trailers per open acre. Not a verified or buildable count.");
        Row("verifiedParking", "Parking", "Verified parking capacity", s => s.VerifiedParking.Status switch
        {
            FeasibilityStatus.Verified when !s.VerifiedParking.Documented => ($"Insufficient evidence — recorded as verified without {s.VerifiedParking.Missing}: {s.VerifiedParking.Evidence ?? "no detail"}", "unknown"),
            FeasibilityStatus.Verified => (s.VerifiedParking.Evidence is { Length: > 0 } e ? $"Verified — {e}" : "Verified (no count recorded)", "verified"),
            FeasibilityStatus.Conflicting => ($"Conflicting evidence — {s.VerifiedParking.Evidence ?? "no detail recorded"}", "concern"),
            FeasibilityStatus.Concern => ($"Concern — {s.VerifiedParking.Evidence ?? "no detail recorded"}", "concern"),
            _ => ("Not verified", "unknown")
        });

        Row("utility", "Power", "Electric utility (source data)", s => (s.Utility ?? "Unknown", s.Utility is null ? "unknown" : "source"));
        Row("electrical", "Power", "Electrical capacity verification", s => Verdict(s.Electrical));

        Row("flood", "Constraints", "Flood status", s => Screen(s.Flood));
        Row("contamination", "Constraints", "Contamination status", s => Screen(s.Contamination));
        Row("highlands", "Constraints", "Highlands status", s => Screen(s.Highlands));
        Row("floodEnvReview", "Constraints", "Flood & environmental review", s => Verdict(s.FloodEnvironmentalReview));

        Row("missing", "Verification", "Missing verification items", s => s.MissingVerification.Count == 0
            ? ("None", "verified") : ($"{s.MissingVerification.Count}: {string.Join("; ", s.MissingVerification)}", "unknown"));
        Row("concerns", "Verification", "Reviewer concerns", s => s.Concerns.Count == 0
            ? ("None recorded", "") : (string.Join("; ", s.Concerns), "concern"));

        Row("maps", "Links", "Google Maps", s => (s.GoogleMapsUrl, ""), comparable: false);
        return rows;
    }

    public static string IosLabel(IosClassification c) => c switch
    {
        IosClassification.Confirmed => "Confirmed (manual review)",
        IosClassification.FreightIndicated => "Freight indicated (unverified)",
        IosClassification.Potential => "Potential (unverified)",
        IosClassification.NotIos => "Not IOS",
        _ => "Unknown"
    };

    private static (string, string) Verdict(VerificationDto v) => v.Status switch
    {
        FeasibilityStatus.Verified when !v.Documented => ($"Insufficient evidence — recorded as verified without {v.Missing}: {v.Evidence ?? "no detail"}", "unknown"),
        FeasibilityStatus.Verified => (v.Evidence is { Length: > 0 } e ? $"Verified — {e}" : "Verified", "verified"),
        FeasibilityStatus.Conflicting => (v.Evidence is { Length: > 0 } e ? $"Conflicting evidence — {e}" : "Conflicting evidence", "concern"),
        FeasibilityStatus.Concern => (v.Evidence is { Length: > 0 } e ? $"Concern — {e}" : "Concern", "concern"),
        _ => ("Not reviewed", "unknown")
    };

    private static (string, string) Screen(ScreeningDto s) => s.Status switch
    {
        ScreeningStatus.NotChecked => ($"Not checked — {s.Detail}", "unknown"),
        ScreeningStatus.Clear => (s.Detail, "clear"),
        ScreeningStatus.Caution => (s.Detail, "caution"),
        _ => (s.Detail, "flag")
    };

    private static string Exit(ComparedSiteDto s)
    {
        if (s.NearestHighway is null) return "Unknown";
        var exit = s.NearestExit is { Length: > 0 } ? $" exit {s.NearestExit}" : "";
        var name = s.ExitName is { Length: > 0 } ? $" ({s.ExitName})" : "";
        return $"{s.NearestHighway}{exit}{name}";
    }

    /// <summary>CSV: one row per attribute, one column per site, plus a "differs" column. UTF-8 with BOM.</summary>
    public static byte[] ToCsv(SiteComparisonDto dto)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', new[] { "group", "attribute" }
            .Concat(dto.Sites.Select(s => $"{s.SiteId} (Seg {s.Segment})"))
            .Append("values_differ").Select(Csv)));
        foreach (var r in dto.Rows)
            sb.AppendLine(string.Join(',', new[] { r.Group, r.Label }.Concat(r.Values)
                .Append(r.Comparable ? (r.Differs ? "yes" : "no") : "").Select(Csv)));
        sb.AppendLine();
        sb.AppendLine(Csv("Note: " + dto.ScoreNote));
        sb.AppendLine(Csv("Note: \"Not reviewed\" / \"Not checked\" / \"Unknown\" mean no verification exists — they are not passes. No site is ranked best by this comparison."));
        if (dto.MissingSiteIds.Count > 0)
            sb.AppendLine(Csv("Not found in the current dataset: " + string.Join(", ", dto.MissingSiteIds)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string I(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);
    private static string Csv(string v) => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
}
