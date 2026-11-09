using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NJTruckCharging.Domain.Methodology;

/// <summary>
/// A complete, declarative C3 scoring methodology: an ordered list of criteria evaluated by
/// <see cref="MethodologyEngine"/>. This is data, never code — every field and operator must come from
/// <see cref="ScoringFieldCatalog"/> and is checked by <see cref="MethodologyValidator"/>.
/// </summary>
public sealed record MethodologyRules
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<Criterion> Criteria { get; init; } = [];

    /// <summary>Stable SHA-256 of the canonical JSON — identifies exactly which rules a preview was computed with.</summary>
    public string ComputeHash() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MethodologyJson.Serialize(this))))[..32];

    public MethodologyRules DeepClone() => MethodologyJson.Deserialize(MethodologyJson.Serialize(this));
}

public enum CriterionKind
{
    /// <summary>Points scale linearly between <see cref="Criterion.FullAt"/> (max points) and <see cref="Criterion.ZeroAt"/> (0).</summary>
    Linear,
    /// <summary>Ordered cases; the first case whose conditions all hold awards its points, else <see cref="Criterion.DefaultPoints"/>.</summary>
    Cases,
    /// <summary>MaxPoints × percentile rank of the field within the site's NOFA segment (ties share the average rank).</summary>
    Percentile
}

public enum CriterionCategory { Access, Site, TruckDemand, ChargerGap, Penalty, Bonus }

public enum MissingDataMode
{
    /// <summary>Sites without a value for a field this criterion uses earn 0 points from it.</summary>
    ZeroPoints,
    /// <summary>Sites without a value earn <see cref="MissingDataPolicy.Points"/> (e.g. a penalty until verified).</summary>
    FixedPoints
}

/// <summary>What a criterion awards when a site lacks data for a field it uses. Values are never imputed.</summary>
public sealed record MissingDataPolicy
{
    public MissingDataMode Mode { get; init; } = MissingDataMode.ZeroPoints;
    public double Points { get; init; }

    public double Award => Mode == MissingDataMode.FixedPoints ? Points : 0;
}

public sealed record Criterion
{
    /// <summary>Stable identifier (lower camel case). Built-in criteria keep the keys of the original engine.</summary>
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Explanation { get; init; } = "";
    public CriterionCategory Category { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>True for the 16 criteria of the original C3 workbook methodology.</summary>
    public bool BuiltIn { get; init; }
    public CriterionKind Kind { get; init; }

    /// <summary>Field scored by Linear and Percentile criteria.</summary>
    public string? Field { get; init; }

    // Linear
    public double? FullAt { get; init; }
    public double? ZeroAt { get; init; }

    /// <summary>Points at <see cref="FullAt"/> (Linear) or at the 100th percentile (Percentile).</summary>
    public double? MaxPoints { get; init; }

    // Cases
    public List<ScoreCase> Cases { get; init; } = [];
    public double DefaultPoints { get; init; }

    public MissingDataPolicy Missing { get; init; } = new();
}

/// <summary>One band/case of a <see cref="CriterionKind.Cases"/> criterion. All conditions must hold (AND).</summary>
public sealed record ScoreCase
{
    public string? Label { get; init; }
    public List<Condition> Conditions { get; init; } = [];
    public double Points { get; init; }
}

public enum ConditionOp
{
    // numeric
    Lt, Lte, Gt, Gte, Eq, Neq, Between,
    // boolean
    IsTrue, IsFalse,
    // category (single value) / lot categories (any lot of a merged site)
    In, NotIn, AnyLotIn
}

public sealed record Condition
{
    public string Field { get; init; } = "";
    public ConditionOp Op { get; init; }
    /// <summary>Numeric operand (lower bound for Between).</summary>
    public double? Value { get; init; }
    /// <summary>Upper bound for Between (inclusive).</summary>
    public double? Value2 { get; init; }
    /// <summary>Category operands for In / NotIn / AnyLotIn.</summary>
    public List<string> Values { get; init; } = [];
}

public static class MethodologyJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static string Serialize(MethodologyRules rules) => JsonSerializer.Serialize(rules, Options);

    public static MethodologyRules Deserialize(string json) =>
        JsonSerializer.Deserialize<MethodologyRules>(json, Options) ?? throw new JsonException("Empty methodology JSON.");
}
