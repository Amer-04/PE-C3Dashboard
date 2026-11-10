namespace NJTruckCharging.Domain.Methodology;

/// <summary>
/// The set of typed data fields a methodology may score: the built-in source-data fields plus any user-defined
/// custom fields (with their recorded/imported values). Criteria can only reference fields in this catalog — the
/// engine never evaluates anything else.
/// </summary>
public sealed class FieldCatalog
{
    public static FieldCatalog Builtin { get; } = new(ScoringFieldCatalog.All);

    private readonly Dictionary<string, ScoringField> _byKey;

    public IReadOnlyList<ScoringField> All { get; }

    public FieldCatalog(IEnumerable<ScoringField> fields)
    {
        All = fields.ToList();
        _byKey = All.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    public ScoringField? Find(string? key) => key is not null && _byKey.TryGetValue(key, out var f) ? f : null;

    /// <summary>Built-in fields plus <paramref name="custom"/> (custom keys can never shadow a built-in key).</summary>
    public static FieldCatalog WithCustom(IEnumerable<ScoringField> custom) =>
        new(ScoringFieldCatalog.All.Concat(custom.Where(c => ScoringFieldCatalog.Find(c.Key) is null)));
}
