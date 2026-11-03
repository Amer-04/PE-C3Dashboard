using NJTruckCharging.Application.Import;

namespace NJTruckCharging.Application.Abstractions;

/// <summary>Parses C3_candidate_sites_all_segments.xlsx.</summary>
public interface IWorkbookReader
{
    ParsedWorkbook Read(Stream xlsx);
}
