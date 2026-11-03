namespace NJTruckCharging.Domain.Entities;

/// <summary>Row from the "Method and sources" sheet.</summary>
public class MethodologyNote
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string Topic { get; set; } = default!;
    public string Detail { get; set; } = default!;
}
