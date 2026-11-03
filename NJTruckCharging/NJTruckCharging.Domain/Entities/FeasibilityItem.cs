namespace NJTruckCharging.Domain.Entities;

/// <summary>The site-feasibility checks a reviewer confirms on the ground / from records.</summary>
public enum FeasibilityItemKey
{
    TruckEntranceAccess = 0,
    TurningManeuveringSpace = 1,
    UsableLand = 2,
    TruckParkingCapacity = 3,
    EligibleHighwayAccess = 4,
    ElectricalService = 5,
    FloodEnvironmental = 6,
    Zoning = 7,
    OwnershipLeaseAvailability = 8,
    PropertyOwnerInterest = 9,
    /// <summary>
    /// Outdoor-storage (IOS) use observed on site. A Verified verdict (with evidence) is the ONLY way a site
    /// becomes IOS <see cref="IosClassification.Confirmed"/>; a Concern marks it Not IOS by manual review.
    /// </summary>
    OutdoorStorageUse = 10,
    /// <summary>
    /// The official parcel (block/lot, address, boundary) was checked against the county tax map / records. Verified
    /// (with evidence) makes the site's evidence <c>Verified</c>; Concern records a parcel mismatch.
    /// </summary>
    ParcelIdentity = 11,
    /// <summary>The business listed in the source data actually operates at this address. Concern records a mismatch.</summary>
    BusinessAtAddress = 12
}

/// <summary>Reviewer's verdict on one feasibility item. Defaults to <see cref="Unknown"/> (not yet checked).</summary>
public enum FeasibilityStatus
{
    Unknown = 0,
    Verified = 1,
    Concern = 2
}

/// <summary>
/// One line of a site's feasibility review. This is an on-the-ground/records verification and is kept
/// entirely separate from the computed C3 score — it never affects scoring or ranking. User-owned:
/// soft-referenced by <see cref="CandidateSite.SiteId"/> (no FK) so it survives workbook re-imports.
/// At most one row per (SiteId, ItemKey).
/// </summary>
public class FeasibilityItem
{
    public int Id { get; set; }

    public string SiteId { get; set; } = default!;
    public FeasibilityItemKey ItemKey { get; set; }

    public FeasibilityStatus Status { get; set; } = FeasibilityStatus.Unknown;

    /// <summary>What was checked and what was found (the evidence for the verdict).</summary>
    public string? Evidence { get; set; }

    public string? Reviewer { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
