using System.Linq.Expressions;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Application.Sites;

/// <summary>The outcome of classifying one site for Industrial Outdoor Storage (IOS).</summary>
/// <param name="Classification">Effective classification (manual review overrides the source-derived one).</param>
/// <param name="Basis">Whether the classification came from source data or a reviewer.</param>
/// <param name="Evidence">Plain-language reasoning for the effective classification.</param>
/// <param name="DerivedClassification">What the source data alone indicates (always populated).</param>
/// <param name="DerivedEvidence">Reasoning behind <paramref name="DerivedClassification"/>.</param>
/// <param name="ReviewStatus">The reviewer's outdoor-storage-use verdict (Unknown = not reviewed).</param>
public sealed record IosAssessment(
    IosClassification Classification,
    IosBasis Basis,
    string Evidence,
    IosClassification DerivedClassification,
    string DerivedEvidence,
    FeasibilityStatus ReviewStatus);

/// <summary>
/// THE single source of truth for IOS classification. Filtering (SQL), the ranked list, the site detail,
/// comparison and CSV exports all go through here, so they can never disagree.
/// <para>
/// Source-derived rule (<see cref="Derived"/>), first match wins:
/// no open-acreage estimate → Unknown; freight/logistics business listed on the parcel + ≥ 1 open acre →
/// FreightIndicated; industrial/vacant class + ≥ 1 open acre → Potential; otherwise NotIos.
/// </para>
/// <para>
/// Manual override: a reviewer's <see cref="FeasibilityItemKey.OutdoorStorageUse"/> verdict. Verified →
/// Confirmed (the ONLY path to Confirmed); Concern → NotIos. Freight activity alone is never "Confirmed".
/// </para>
/// </summary>
public static class IosRules
{
    /// <summary>Minimum open acreage for a site to qualify as IOS Potential / FreightIndicated.</summary>
    public const double MinOpenAcres = 1.0;

    /// <summary>The feasibility check that records manual IOS confirmation.</summary>
    public const FeasibilityItemKey ReviewKey = FeasibilityItemKey.OutdoorStorageUse;

    /// <summary>Industrial (4B) or vacant (1) land, including merged-lot combinations such as "1/4B".</summary>
    public static readonly Expression<Func<CandidateSite, bool>> IsIndustrialOrVacant =
        s => s.PropClass.Contains("4B") || s.PropClass == "1" || s.PropClass.StartsWith("1/");

    /// <summary>Source-derived classification as an EF-translatable expression (used in SQL and, compiled, in memory).</summary>
    public static readonly Expression<Func<CandidateSite, IosClassification>> Derived = BuildDerived();

    private static readonly Func<CandidateSite, IosClassification> DerivedCompiled = Derived.Compile();

    private static Expression<Func<CandidateSite, IosClassification>> BuildDerived()
    {
        Expression<Func<CandidateSite, bool, IosClassification>> rule = (s, indVac) =>
            s.OpenAcresEst <= 0 ? IosClassification.Unknown
            : s.FreightUse && s.OpenAcresEst >= MinOpenAcres ? IosClassification.FreightIndicated
            : s.OpenAcresEst >= MinOpenAcres && indVac ? IosClassification.Potential
            : IosClassification.NotIos;
        // Inline IsIndustrialOrVacant so the class rule lives in exactly one place.
        var s = rule.Parameters[0];
        var body = new ReplaceVisitor(rule.Parameters[1], Inline(IsIndustrialOrVacant, s)).Visit(rule.Body)!;
        return Expression.Lambda<Func<CandidateSite, IosClassification>>(body, s);
    }

    /// <summary>The source-derived classification of one site (in memory).</summary>
    public static IosClassification Classify(CandidateSite s) => DerivedCompiled(s);

    /// <summary>
    /// Applies a reviewer's outdoor-storage verdict on top of a source-derived class.
    /// <para>
    /// Current verification rule (unchanged by Phase 1, deliberately): any stored <c>OutdoorStorageUse</c> verdict
    /// of Verified makes the site IOS <c>Confirmed</c> (ManualReview); Concern makes it <c>NotIos</c>; anything else
    /// (Unknown, Conflicting) leaves the source-derived class. Saving a new Verified verdict requires evidence text
    /// (FeasibilityService), but this classification does NOT check the Phase 1 evidence standard
    /// (<see cref="Research.ResearchQuality.IsDocumented"/>: source URL, evidence date, Recorded-by name), so a legacy
    /// or undocumented verdict still confirms IOS. Changing that would move sites between IOS classes and filters;
    /// it needs a separate, approved decision.
    /// </para>
    /// </summary>
    public static (IosClassification Classification, IosBasis Basis) Resolve(IosClassification derived, FeasibilityStatus? review) =>
        review switch
        {
            FeasibilityStatus.Verified => (IosClassification.Confirmed, IosBasis.ManualReview),
            FeasibilityStatus.Concern => (IosClassification.NotIos, IosBasis.ManualReview),
            _ => (derived, IosBasis.SourceDerived)
        };

    /// <summary>Full assessment (class, basis, evidence) for one site and its optional outdoor-storage review.</summary>
    public static IosAssessment Assess(CandidateSite s, FeasibilityItem? review)
    {
        var derived = Classify(s);
        var derivedEvidence = DerivedEvidence(s, derived);
        var status = review?.Status ?? FeasibilityStatus.Unknown;
        var (cls, basis) = Resolve(derived, status);
        var evidence = basis switch
        {
            IosBasis.ManualReview when status == FeasibilityStatus.Verified =>
                $"Manually confirmed by site review: {review!.Evidence ?? "(no evidence text recorded)"}",
            IosBasis.ManualReview =>
                $"Reviewer recorded a concern — not suitable for outdoor storage: {review!.Evidence ?? "(no evidence text recorded)"}",
            _ => derivedEvidence + " Inferred from source data — not manually verified."
        };
        return new IosAssessment(cls, basis, evidence, derived, derivedEvidence, status);
    }

    /// <summary>Plain-language reasoning for a source-derived class (mirrors <see cref="Derived"/>).</summary>
    public static string DerivedEvidence(CandidateSite s, IosClassification derived)
    {
        var openTxt = $"{s.OpenAcresEst:0.#} open ac";
        return derived switch
        {
            IosClassification.Unknown => "No open-acreage estimate available — IOS suitability can't be assessed.",
            IosClassification.FreightIndicated =>
                $"Freight/logistics business listed on the parcel with {openTxt} — indicates possible truck-yard / outdoor-storage use (freight activity alone does not confirm IOS).",
            IosClassification.Potential => $"Industrial/vacant land with {openTxt}; outdoor-storage use unknown.",
            _ when s.OpenAcresEst < MinOpenAcres => $"Only {openTxt} — below the {MinOpenAcres:0.#}-acre threshold for outdoor storage.",
            _ => $"{openTxt}, but property class {s.PropClass} is not industrial/vacant."
        };
    }

    /// <summary>Parses filter tokens (confirmed | indicated | potential | not | unknown) to classes.</summary>
    public static IosClassification[] ParseTokens(IEnumerable<string> tokens) =>
        tokens.Select(t => t.Trim().ToLowerInvariant() switch
            {
                "confirmed" => (IosClassification?)IosClassification.Confirmed,
                "indicated" or "freightindicated" => IosClassification.FreightIndicated,
                "potential" => IosClassification.Potential,
                "not" or "notios" => IosClassification.NotIos,
                "unknown" => IosClassification.Unknown,
                _ => null
            })
            .Where(c => c is not null).Select(c => c!.Value).Distinct().ToArray();

    /// <summary>
    /// SQL predicate: the site's EFFECTIVE classification is one of <paramref name="wanted"/>. With
    /// <paramref name="reviews"/> the manual outdoor-storage verdict overrides the derived class; without it
    /// (no evaluation data available) only the derived class is used and nothing can be Confirmed.
    /// </summary>
    public static Expression<Func<CandidateSite, bool>> InClasses(
        IReadOnlyCollection<IosClassification> wanted, IQueryable<FeasibilityItem>? reviews)
    {
        var s = Expression.Parameter(typeof(CandidateSite), "s");
        Expression effective = Inline(Derived, s);
        if (reviews is not null)
        {
            Expression<Func<CandidateSite, bool>> verified = x =>
                reviews.Any(f => f.SiteId == x.SiteId && f.ItemKey == ReviewKey && f.Status == FeasibilityStatus.Verified);
            Expression<Func<CandidateSite, bool>> concern = x =>
                reviews.Any(f => f.SiteId == x.SiteId && f.ItemKey == ReviewKey && f.Status == FeasibilityStatus.Concern);
            effective = Expression.Condition(Inline(verified, s), Expression.Constant(IosClassification.Confirmed),
                Expression.Condition(Inline(concern, s), Expression.Constant(IosClassification.NotIos), effective));
        }

        Expression? any = null;
        foreach (var c in wanted.Distinct())
        {
            var eq = Expression.Equal(effective, Expression.Constant(c));
            any = any is null ? eq : Expression.OrElse(any, eq);
        }
        return Expression.Lambda<Func<CandidateSite, bool>>(any ?? Expression.Constant(false), s);
    }

    /// <summary>
    /// Builds a projection that receives the source-derived IOS class as its second argument (inlined from
    /// <see cref="Derived"/>, so it is computed in SQL with the same rule).
    /// </summary>
    public static Expression<Func<CandidateSite, T>> WithDerived<T>(Expression<Func<CandidateSite, IosClassification, T>> projection)
    {
        var s = projection.Parameters[0];
        var body = new ReplaceVisitor(projection.Parameters[1], Inline(Derived, s)).Visit(projection.Body)!;
        return Expression.Lambda<Func<CandidateSite, T>>(body, s);
    }

    private static Expression Inline<TResult>(Expression<Func<CandidateSite, TResult>> lambda, ParameterExpression s) =>
        new ReplaceVisitor(lambda.Parameters[0], s).Visit(lambda.Body)!;

    private sealed class ReplaceVisitor(Expression from, Expression to) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node) => node == from ? to : base.Visit(node);
    }
}
