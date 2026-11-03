using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Feasibility;

/// <summary>Human-readable names for the feasibility checks (mirrors FEASIBILITY_ITEMS in the Angular app).</summary>
public static class FeasibilityLabels
{
    public static string For(FeasibilityItemKey key) => key switch
    {
        FeasibilityItemKey.TruckEntranceAccess => "Truck entrance & access",
        FeasibilityItemKey.TurningManeuveringSpace => "Turning & maneuvering space",
        FeasibilityItemKey.UsableLand => "Actual usable land",
        FeasibilityItemKey.TruckParkingCapacity => "Truck parking capacity",
        FeasibilityItemKey.EligibleHighwayAccess => "Eligible highway access",
        FeasibilityItemKey.ElectricalService => "Electrical service / capacity",
        FeasibilityItemKey.FloodEnvironmental => "Flood & environmental constraints",
        FeasibilityItemKey.Zoning => "Zoning",
        FeasibilityItemKey.OwnershipLeaseAvailability => "Ownership / lease availability",
        FeasibilityItemKey.PropertyOwnerInterest => "Property-owner interest",
        FeasibilityItemKey.OutdoorStorageUse => "Outdoor-storage (IOS) use",
        FeasibilityItemKey.ParcelIdentity => "Official parcel identification",
        FeasibilityItemKey.BusinessAtAddress => "Business operating at the address",
        _ => key.ToString()
    };
}
