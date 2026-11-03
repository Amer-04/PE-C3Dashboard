namespace NJTruckCharging.Domain.Entities;

/// <summary>
/// Industrial Outdoor Storage (IOS) classification. Only <see cref="Confirmed"/> means a person verified
/// outdoor-storage use; every other value is inferred from source data (parcel class, open-acreage estimate,
/// business listings) and must not be presented as verified.
/// <list type="bullet">
/// <item><see cref="Confirmed"/> — MANUALLY confirmed: a reviewer marked the
/// <see cref="FeasibilityItemKey.OutdoorStorageUse"/> feasibility check Verified, with evidence.</item>
/// <item><see cref="FreightIndicated"/> — source-derived: a freight/logistics business is listed on the parcel
/// and there is open land. A strong signal, but freight activity is NOT proof of outdoor-storage use.</item>
/// <item><see cref="Potential"/> — source-derived: industrial or vacant land with enough open acreage; use unknown.</item>
/// <item><see cref="NotIos"/> — source data says it does not qualify (commercial / too little open land), or a
/// reviewer recorded a Concern on the outdoor-storage check.</item>
/// <item><see cref="Unknown"/> — not enough data to assess (no open-acreage estimate).</item>
/// </list>
/// </summary>
public enum IosClassification
{
    Unknown = 0,
    NotIos = 1,
    Potential = 2,
    FreightIndicated = 3,
    Confirmed = 4
}

/// <summary>Where an IOS classification came from.</summary>
public enum IosBasis
{
    /// <summary>Inferred from the source dataset (parcel class, open acreage, business listings). Not verified.</summary>
    SourceDerived = 0,
    /// <summary>Set by a reviewer's outdoor-storage-use feasibility verdict (Verified or Concern).</summary>
    ManualReview = 1
}
