namespace NJTruckCharging.Domain.Entities;

/// <summary>NOFA segment (Table 2) with the workbook's summary statistics ("Summary" sheet).</summary>
public class SegmentInfo
{
    public int Id { get; set; }                       // segment number 1..6
    public string Counties { get; set; } = default!;
    public int Candidates { get; set; }
    public int MergedSites { get; set; }
    public int TruckingOnSite { get; set; }
    public double Top100MedianMilesToExit { get; set; }
    public double Top100MedianOpenAcres { get; set; }
    public int Top100TruckingOnSite { get; set; }
    public double Top100MedianTrucksBased5Mi { get; set; }
    public double Top100MedianTruckAadtExit { get; set; }
    public int Top100MergedSites { get; set; }
    public int Top100Flood { get; set; }
    public int Top100Contaminated { get; set; }
    public int Top100WithinTwoMilesOfPowerEdison { get; set; }
    public string? No1Site { get; set; }
    public string? No1Municipality { get; set; }
    public string? No1Businesses { get; set; }
    public string? No1Highway { get; set; }
    public double? No1MilesToExit { get; set; }
    public string? PowerEdisonSitesInSegment { get; set; }
}
