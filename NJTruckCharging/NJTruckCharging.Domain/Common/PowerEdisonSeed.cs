using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Domain.Common;

/// <summary>
/// Power Edison / EV Edison sites referenced by the workbook. Coordinates were back-solved from the workbook's
/// miles_to_pe_site column (residual &lt; 0.01 mi), so they line up with the distances used in the analysis.
/// </summary>
public static class PowerEdisonSeed
{
    public static IReadOnlyList<PowerEdisonSite> Sites { get; } =
    [
        new()
        {
            Name = "Kearny Point hub", ShortName = "Kearny Point", Municipality = "Kearny Town", Segment = 4,
            Status = PowerEdisonSiteStatus.Operating, Address = "Kearny Point Industrial Park, Kearny, NJ",
            Notes = "EV Edison flagship multi-class hub (opened 2025): 30 × 180 kW ports, ~200 heavy-duty sessions/day in Phase I; developed with Hugo Neu.",
            Latitude = 40.72620, Longitude = -74.10100
        },
        new()
        {
            Name = "Irvington LOI (133 S 20th St)", ShortName = "Irvington", Municipality = "Irvington Twp", Segment = 4,
            Status = PowerEdisonSiteStatus.LetterOfIntent, Address = "133 S 20th St, Irvington, NJ",
            Notes = "Letter of intent.", Latitude = 40.71920, Longitude = -74.22109
        },
        new()
        {
            Name = "Edison LOI (Talmadge Rd)", ShortName = "Edison", Municipality = "Edison Twp", Segment = 3,
            Status = PowerEdisonSiteStatus.LetterOfIntent, Address = "Talmadge Rd, Edison, NJ",
            Notes = "Letter of intent.", Latitude = 40.53690, Longitude = -74.39640
        },
        new()
        {
            Name = "Paterson (E 30th St / 18th Ave)", ShortName = "Paterson", Municipality = "Paterson City", Segment = 6,
            Status = PowerEdisonSiteStatus.Planned, Address = "E 30th St / 18th Ave, Paterson, NJ",
            Notes = "Pipeline site.", Latitude = 40.91020, Longitude = -74.14741
        }
    ];
}
