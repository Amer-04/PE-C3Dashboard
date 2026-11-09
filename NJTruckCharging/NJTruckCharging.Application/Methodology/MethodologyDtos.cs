using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

public sealed record VersionSummaryDto(
    int Id, string Name, MethodologyVersionStatus Status, bool IsBaseline, bool IsActive, bool IsPreviousActive,
    string RulesHash, string? ChangeSummary, int? BasedOnVersionId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? PublishedAtUtc, DateTimeOffset? PreviewedAtUtc,
    /// <summary>Stored proposed scores match the current rules AND current data (publishable).</summary>
    bool PreviewCurrent,
    int CriteriaCount, int EnabledCount, bool IdenticalToBaseline);

public sealed record CriterionViewDto(
    string Key, string Name, string Category, bool Enabled, bool BuiltIn, CriterionKind Kind,
    string Rule, string Explanation, double Min, double Max, IReadOnlyList<string> Fields,
    bool UsesMissableData, string? MissingPolicy);

public sealed record ActiveMethodologyDto(
    VersionSummaryDto Version, IReadOnlyList<CriterionViewDto> Criteria, double MinPossible, double MaxPossible,
    int? PreviousActiveVersionId, DateTimeOffset? ActivatedAtUtc, int DataVersion, string TieBreak);

public sealed record VersionDetailDto(
    VersionSummaryDto Version, MethodologyRules Rules, IReadOnlyList<CriterionViewDto> Criteria,
    double MinPossible, double MaxPossible, IReadOnlyList<RuleChangeDto> ChangesVsActive,
    IReadOnlyList<ValidationIssue> Issues, PreviewSummaryDto? Preview);

public sealed record FieldDto(
    string Key, string Label, FieldType Type, string? Unit, string Group, string Description, string? MissingMeaning,
    IReadOnlyList<string>? Categories, IReadOnlyList<ConditionOp> Operators,
    int Present, int Missing, int Zero, double? Min, double? Max, IReadOnlyDictionary<string, int>? CategoryCounts)
{
    /// <summary>User-defined custom field (manual entry or imported dataset).</summary>
    public bool IsCustom { get; init; }
    public CustomFieldSource? CustomSource { get; init; }
}

public sealed record ValidationResultDto(bool Valid, IReadOnlyList<ValidationIssue> Issues, double MinPossible, double MaxPossible);

public sealed record MoverDto(
    string SiteId, string Address, string Municipality, int Segment,
    double ActiveScore, int ActiveRank, double ProposedScore, int ProposedRank,
    double? BaselineScore, int? BaselineRank);

public sealed record SegmentPreviewDto(
    int Segment, int Sites, double AvgActive, double AvgProposed, int RanksChanged, int TopNKept,
    IReadOnlyList<MoverDto> Entering, IReadOnlyList<MoverDto> Leaving, IReadOnlyList<MoverDto> ProposedTop);

public sealed record CriterionImpactDto(
    string Key, string Name, bool Enabled, double AvgPoints, double MinPoints, double MaxPoints,
    int SitesNonZero, int SitesMissingData, string? MissingPolicy);

public sealed record PreviewSummaryDto(
    int VersionId, string VersionName, string RulesHash, int DataVersion, DateTimeOffset ComputedAtUtc, long ElapsedMs, int Sites,
    int ComparedToVersionId, string ComparedToName, bool IdenticalToActive,
    int ScoresChanged, int RanksChanged, double MaxIncrease, double MaxDecrease, double MeanAbsChange,
    int TopN, IReadOnlyList<SegmentPreviewDto> Segments, IReadOnlyList<MoverDto> TopRisers, IReadOnlyList<MoverDto> TopFallers,
    IReadOnlyList<CriterionImpactDto> Criteria, IReadOnlyList<RuleChangeDto> Changes, IReadOnlyList<ValidationIssue> Warnings);

public sealed record ComparisonPageDto(int Total, int Page, int PageSize, int VersionId, int? ActiveVersionId, IReadOnlyList<MoverDto> Rows);

public sealed record ActivationDto(int Id, int VersionId, string VersionName, int? PreviousVersionId, ActivationReason Reason,
    DateTimeOffset AtUtc, int SitesScored, string? Note);

// ---- Requests --------------------------------------------------------------------------------------------------
public sealed record CreateDraftRequest(string? Name, int? BasedOnVersionId);
public sealed record UpdateDraftRequest(string Name, string? ChangeSummary, MethodologyRules Rules);
public sealed record PublishRequest(bool Confirm, int? ExpectedActiveVersionId, string RulesHash, string? Note);

/// <summary>Outcome of a service call that maps onto an HTTP status.</summary>
public enum MethodologyError { None, NotFound, Invalid, Conflict, Forbidden }

public sealed record MethodologyOutcome<T>(T? Value, MethodologyError Error = MethodologyError.None, string? Message = null,
    IReadOnlyList<ValidationIssue>? Issues = null)
{
    public static MethodologyOutcome<T> Ok(T value) => new(value);
    public static MethodologyOutcome<T> Fail(MethodologyError e, string msg, IReadOnlyList<ValidationIssue>? issues = null) => new(default, e, msg, issues);
}

public sealed record PublishResultDto(int ActiveVersionId, int? PreviousVersionId, int SitesUpdated, long ElapsedMs);
