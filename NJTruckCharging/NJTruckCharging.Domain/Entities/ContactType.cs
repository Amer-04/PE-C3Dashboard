namespace NJTruckCharging.Domain.Entities;

/// <summary>Role of a researched contact relative to a C3 charging decision at a site.</summary>
public enum ContactType
{
    PropertyOwner = 0,
    Broker = 1,
    FacilityManager = 2,
    SustainabilityManager = 3,
    EnergyEfficiencyManager = 4,
    FleetRepresentative = 5,
    Other = 6
}

/// <summary>
/// How confident the team is in a researched contact. A contact starts <see cref="Unverified"/>
/// (a potential lead that still needs checking) and becomes <see cref="Verified"/> once confirmed
/// from a credible source. <see cref="Invalid"/> records a lead that was checked and found wrong.
/// </summary>
public enum ContactVerificationStatus
{
    Unverified = 0,
    Verified = 1,
    Invalid = 2
}
