using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

/// <summary>
/// Editable methodology management: drafts, validation, full-dataset preview, atomic publish/restore and history.
/// The live score columns change ONLY through <see cref="IMethodologyStore.ActivateAsync"/> (or a re-import that
/// re-applies the active version), never on a draft edit.
/// </summary>
public sealed class MethodologyService(
    IMethodologyStore store,
    ICustomFieldStore customFields,
    IReadStore read,
    IWriteStore write,
    ActiveMethodologyCache cache,
    FieldStatsCache fieldStats,
    ScoringJobGate gate,
    TimeProvider clock,
    ILogger<MethodologyService> logger)
{
    /// <summary>
    /// Proposed scores are kept for the baseline, the active and previous versions and this many most recently reviewed
    /// other versions; older ones are pruned (each copy is one row per site) and must be reviewed again to publish.
    /// </summary>
    public const int KeepReviewedVersions = 5;

    public const string TieBreak =
        "Within each segment: score (high → low), then distance to exit (closer first), then the original workbook rank, then Site ID.";
    public const int MaxTopN = 50;

    // =============================================================================================================
    // Initialisation
    // =============================================================================================================

    /// <summary>
    /// Creates immutable baseline Version 1 on first run and activates it only after proving it reproduces the stored
    /// live scores and ranks for every site. Idempotent.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        await store.EnsureStateAsync(ct);
        var now = clock.GetUtcNow();
        var baseline = await store.FindBaselineAsync(ct);
        if (baseline is null)
        {
            var rules = BaselineMethodology.Create();
            baseline = new MethodologyVersion
            {
                Name = BaselineMethodology.Name, Status = MethodologyVersionStatus.Published, IsBaseline = true,
                RulesJson = MethodologyJson.Serialize(rules), RulesHash = rules.ComputeHash(),
                ChangeSummary = "Original C3 workbook scoring rules (immutable baseline, reproduces the workbook for every site).",
                CreatedAtUtc = now, UpdatedAtUtc = now, PublishedAtUtc = now
            };
            await store.AddVersionAsync(baseline, ct);
            logger.LogInformation("Created baseline methodology version {Id}", baseline.Id);
        }

        var state = (await store.GetStateAsync(ct))!;
        if (state.ActiveVersionId is null)
        {
            var sites = await store.LoadSitesForScoringAsync(ct);
            var rules = RulesOf(baseline);
            var result = MethodologyEngine.Evaluate(sites, rules);
            var mismatches = sites.Zip(result.Sites).Count(p => p.First.Score != p.Second.Score || p.First.ComputedRank != p.Second.Rank);
            if (mismatches > 0)
            {
                // Never silently overwrite live scores: leave the methodology inactive and report it.
                logger.LogError("Baseline methodology does NOT reproduce {Mismatches} of {Sites} stored scores/ranks; not activated.", mismatches, sites.Count);
                await RefreshCacheAsync(ct);
                return;
            }
            var stored = await store.StoreScoresAsync(baseline.Id, MethodologyScoring.ToRows(baseline.Id, rules, result),
                baseline.RulesHash, state.DataVersion, null, now, ct);
            if (stored != ActivationFailure.None) { logger.LogError("Could not store baseline scores: {Failure}", stored); return; }
            var act = await store.ActivateAsync(new ActivateCommand(baseline.Id, null, baseline.RulesHash, ActivationReason.Initial, now,
                $"Baseline verified against {sites.Count:N0} stored scores and ranks (0 mismatches)."), ct);
            if (!act.Ok) logger.LogError("Could not activate baseline: {Message}", act.Message);
            else logger.LogInformation("Baseline methodology activated after verifying {Sites} sites", sites.Count);
        }
        await RefreshCacheAsync(ct);
    }

    public async Task RefreshCacheAsync(CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        if (state?.ActiveVersionId is not { } id) { cache.Set(null); return; }
        var v = await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == id), ct);
        if (v is null) { cache.Set(null); return; }
        var rules = RulesOf(v);
        var (min, max) = MethodologyEngine.PossibleRange(rules, await DefinitionsAsync(ct));
        cache.Set(new ActiveMethodology(v.Id, v.Name, v.IsBaseline, rules, v.RulesHash, state.ActivatedAtUtc, min, max));
    }

    // =============================================================================================================
    // Reads
    // =============================================================================================================

    public async Task<ActiveMethodologyDto?> GetActiveAsync(CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        if (state?.ActiveVersionId is not { } id) return null;
        var v = await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == id), ct);
        if (v is null) return null;
        var rules = RulesOf(v);
        var catalog = await DefinitionsAsync(ct);
        var (min, max) = MethodologyEngine.PossibleRange(rules, catalog);
        return new ActiveMethodologyDto(await SummaryAsync(v, state, ct), Views(rules, catalog), min, max,
            state.PreviousActiveVersionId, state.ActivatedAtUtc, state.DataVersion, TieBreak);
    }

    public async Task<List<VersionSummaryDto>> ListVersionsAsync(CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        var versions = await read.ToListAsync(read.MethodologyVersions.OrderByDescending(v => v.Id), ct);
        var baselineHash = versions.FirstOrDefault(v => v.IsBaseline)?.RulesHash;
        return versions.Select(v => Summary(v, state, baselineHash)).ToList();
    }

    public async Task<VersionDetailDto?> GetVersionAsync(int id, CancellationToken ct = default)
    {
        var v = await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == id), ct);
        if (v is null) return null;
        var state = await store.GetStateAsync(ct);
        var rules = RulesOf(v);
        var catalog = await DefinitionsAsync(ct);
        var (min, max) = MethodologyEngine.PossibleRange(rules, catalog);
        var activeRules = cache.Current?.Rules;
        var preview = v.PreviewSummaryJson is null ? null : JsonSerializer.Deserialize<PreviewSummaryDto>(v.PreviewSummaryJson, MethodologyJson.Options);
        return new VersionDetailDto(await SummaryAsync(v, state, ct), rules, Views(rules, catalog), min, max,
            activeRules is null ? [] : RuleDiff.Compare(activeRules, rules, catalog), MethodologyValidator.Validate(rules, catalog), preview);
    }

    public async Task<List<ActivationDto>> GetActivationsAsync(CancellationToken ct = default)
    {
        var rows = await read.ToListAsync(
            from a in read.MethodologyActivations
            join v in read.MethodologyVersions on a.VersionId equals v.Id
            orderby a.Id descending
            select new ActivationDto(a.Id, a.VersionId, v.Name, a.PreviousVersionId, a.Reason, a.AtUtc, a.SitesScored, a.Note)
            { ActorUserId = a.ActorUserId, ActorEmail = a.ActorEmail }, ct);
        return rows;
    }

    /// <summary>The audit trail, newest first (optionally for one version).</summary>
    public async Task<ChangeLogPageDto> GetChangesAsync(int? versionId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var q = read.MethodologyChanges;
        if (versionId is { } vid) q = q.Where(c => c.VersionId == vid);
        var total = await read.CountAsync(q, ct);
        var rows = await read.ToListAsync(q.OrderByDescending(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new ChangeLogPageDto(total, page, pageSize, rows.Select(c => new ChangeLogEntryDto(
            c.Id, c.VersionId, c.VersionName, c.Action, c.AtUtc, c.ActorUserId, c.ActorEmail, c.RevisionBefore, c.RevisionAfter,
            c.RulesHashBefore, c.RulesHashAfter, c.PreviousActiveVersionId,
            c.ChangesJson is null ? [] : JsonSerializer.Deserialize<List<RuleChangeDto>>(c.ChangesJson, MethodologyJson.Options) ?? [],
            c.Note, c.BeforeJson is not null || c.AfterJson is not null)).ToList());
    }

    /// <summary>Full previous/new state recorded for one audited save or deletion.</summary>
    public async Task<ChangeSnapshotDto?> GetChangeSnapshotAsync(long id, CancellationToken ct = default)
    {
        var c = await read.FirstOrDefaultAsync(read.MethodologyChanges.Where(x => x.Id == id), ct);
        if (c is null) return null;
        static VersionSnapshot? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize<VersionSnapshot>(json, MethodologyJson.Options);
        return new ChangeSnapshotDto(c.Id, Parse(c.BeforeJson), Parse(c.AfterJson));
    }

    public async Task<IReadOnlyList<FieldDto>> GetFieldsAsync(CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        var siteCount = await read.CountAsync(read.Sites, ct);
        var key = (state?.DataVersion ?? 0, siteCount);
        if (!fieldStats.TryGet(key, out var builtIn))
        {
            var sites = await store.LoadSitesForScoringAsync(ct);
            builtIn = ScoringFieldCatalog.All.Select(f => FieldStats(f, sites)).ToList();
            fieldStats.Set(key, builtIn);
        }
        // Custom fields are always computed fresh: their values change through Site Details / imports.
        var defs = await customFields.ListFieldsAsync(ct);
        if (defs.Count == 0) return builtIn;
        var siteIds = await customFields.SiteIdsAsync(ct);
        var values = await customFields.AllValuesAsync(ct);
        var catalog = CustomCatalog.Build(defs, values);
        var custom = defs.Select(d =>
        {
            var f = catalog.Find(d.Key)!;
            var vs = values.Where(v => v.FieldKey == d.Key && siteIds.Contains(v.SiteId)).Select(v => CustomFields.ToFieldValue(d, v)).ToList();
            var nums = vs.Where(v => v.Number is not null).Select(v => v.Number!.Value).ToList();
            Dictionary<string, int>? cats = d.Type switch
            {
                CustomFieldType.Boolean => new() { ["yes"] = vs.Count(v => v.Flag == true), ["no"] = vs.Count(v => v.Flag == false) },
                CustomFieldType.Category => d.CategoryList.ToDictionary(c => c, c => vs.Count(v => string.Equals(v.Text, c, StringComparison.OrdinalIgnoreCase))),
                _ => null
            };
            return new FieldDto(f.Key, f.Label, f.Type, f.Unit, f.Group, f.Description, f.MissingMeaning, f.Categories,
                ScoringFieldCatalog.OperatorsFor(f.Type), vs.Count, siteIds.Count - vs.Count, nums.Count(n => n == 0),
                nums.Count == 0 ? null : nums.Min(), nums.Count == 0 ? null : nums.Max(), cats)
            { IsCustom = true, CustomSource = d.Source };
        });
        return builtIn.Concat(custom).ToList();
    }

    public async Task<ValidationResultDto> ValidateAsync(MethodologyRules? rules, CancellationToken ct = default)
    {
        var catalog = await DefinitionsAsync(ct);
        var issues = MethodologyValidator.Validate(rules, catalog);
        var valid = issues.All(i => i.Severity != IssueSeverity.Error);
        var (min, max) = valid && rules is not null ? MethodologyEngine.PossibleRange(rules, catalog) : (0, 0);
        return new ValidationResultDto(valid, issues, min, max);
    }

    /// <summary>Built-in + custom field DEFINITIONS (no values) - enough to validate and describe rules.</summary>
    private async Task<FieldCatalog> DefinitionsAsync(CancellationToken ct) =>
        CustomCatalog.Build(await customFields.ListFieldsAsync(ct), []);

    /// <summary>Built-in + custom fields WITH every site's current custom values - used for scoring.</summary>
    private Task<FieldCatalog> ScoringCatalogAsync(CancellationToken ct) => CustomCatalog.BuildAsync(customFields, null, ct);

    /// <summary>Paged proposed vs active (vs baseline) scores and ranks for a previewed version.</summary>
    public async Task<ComparisonPageDto?> GetComparisonAsync(int id, int? segment, string? sort, string? search, int page, int pageSize,
        CancellationToken ct = default)
    {
        if (await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == id), ct) is null) return null;
        var baselineId = (await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(v => v.IsBaseline), ct))?.Id ?? -1;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = from v in read.SiteVersionScores.Where(v => v.MethodologyVersionId == id)
                join c in read.Sites on v.SiteId equals c.SiteId
                join b0 in read.SiteVersionScores.Where(b => b.MethodologyVersionId == baselineId) on v.SiteId equals b0.SiteId into bj
                from b in bj.DefaultIfEmpty()
                select new { v.SiteId, c.SiteAddress, c.Municipality, v.Segment, ActiveScore = c.Score, ActiveRank = c.ComputedRank,
                    ProposedScore = v.Score, ProposedRank = v.Rank, BaselineScore = (double?)b.Score, BaselineRank = (int?)b.Rank };
        if (segment is { } seg) q = q.Where(x => x.Segment == seg);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();   // case-insensitive on SQL Server and PostgreSQL alike
            q = q.Where(x => x.SiteId.ToUpper().Contains(s) || x.SiteAddress.ToUpper().Contains(s) || x.Municipality.ToUpper().Contains(s));
        }
        q = sort switch
        {
            "rise" => q.OrderByDescending(x => x.ActiveRank - x.ProposedRank).ThenBy(x => x.Segment).ThenBy(x => x.ProposedRank),
            "fall" => q.OrderBy(x => x.ActiveRank - x.ProposedRank).ThenBy(x => x.Segment).ThenBy(x => x.ProposedRank),
            "change" => q.OrderByDescending(x => Math.Abs(x.ProposedScore - x.ActiveScore)).ThenBy(x => x.Segment).ThenBy(x => x.ProposedRank),
            "active" => q.OrderBy(x => x.Segment).ThenBy(x => x.ActiveRank),
            _ => q.OrderBy(x => x.Segment).ThenBy(x => x.ProposedRank)
        };
        var total = await read.CountAsync(q, ct);
        var rows = await read.ToListAsync(q.Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new ComparisonPageDto(total, page, pageSize, id, cache.Current?.VersionId,
            rows.Select(x => new MoverDto(x.SiteId, x.SiteAddress, x.Municipality, x.Segment, x.ActiveScore, x.ActiveRank,
                x.ProposedScore, x.ProposedRank, x.BaselineScore, x.BaselineRank)).ToList());
    }

    // =============================================================================================================
    // Drafts (admin)
    // =============================================================================================================

    public async Task<MethodologyOutcome<VersionDetailDto>> CreateDraftAsync(CreateDraftRequest req, MethodologyActor actor, CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        var sourceId = req.BasedOnVersionId ?? state?.ActiveVersionId;
        if (sourceId is null) return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Conflict, "No active methodology to copy.");
        var source = await store.FindVersionAsync(sourceId.Value, ct);
        if (source is null) return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.NotFound, "Source version not found.");

        var name = string.IsNullOrWhiteSpace(req.Name) ? $"Draft based on v{source.Id}" : req.Name.Trim();
        if (name.Length > 120) return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Invalid, "Name is limited to 120 characters.");
        var now = clock.GetUtcNow();
        var draft = new MethodologyVersion
        {
            Name = name, Status = MethodologyVersionStatus.Draft, RulesJson = source.RulesJson, RulesHash = source.RulesHash,
            BasedOnVersionId = source.Id, CreatedAtUtc = now, UpdatedAtUtc = now, Revision = 1,
            CreatedByUserId = actor.UserId, CreatedByEmail = actor.Email, UpdatedByUserId = actor.UserId, UpdatedByEmail = actor.Email
        };
        await store.AddVersionAsync(draft, ct);
        store.AddChange(new MethodologyChange
        {
            VersionId = draft.Id, VersionName = draft.Name, Action = MethodologyChangeAction.CreateDraft, AtUtc = now,
            ActorUserId = actor.UserId, ActorEmail = actor.Email, RevisionAfter = draft.Revision, RulesHashAfter = draft.RulesHash,
            Note = $"Copied from v{source.Id} ({source.Name})",
            // Before = the version it was copied from; after = the new draft (identical rules at creation).
            BeforeJson = JsonSerializer.Serialize(new VersionSnapshot(source.Name, source.ChangeSummary, RulesOf(source)), MethodologyJson.Options),
            AfterJson = JsonSerializer.Serialize(new VersionSnapshot(draft.Name, draft.ChangeSummary, RulesOf(draft)), MethodologyJson.Options),
        });
        await store.SaveChangesAsync(ct);
        return MethodologyOutcome<VersionDetailDto>.Ok((await GetVersionAsync(draft.Id, ct))!);
    }

    public async Task<MethodologyOutcome<VersionDetailDto>> UpdateDraftAsync(int id, UpdateDraftRequest req, MethodologyActor actor, CancellationToken ct = default)
    {
        if (req.ExpectedRevision is null)
            return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Invalid, "expectedRevision (the draft revision the edit is based on) is required.");
        var v = await store.FindVersionAsync(id, ct);
        if (v is null) return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.NotFound, MethodologyCodes.DraftConflict,
            "This draft no longer exists (it was deleted by someone else). Reload the page.");
        if (v.Status != MethodologyVersionStatus.Draft || v.IsBaseline)
            return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Conflict, "Only drafts can be edited; published versions (and baseline Version 1) are immutable. Duplicate it as a draft instead.");
        if (v.Revision != req.ExpectedRevision) return DraftConflict<VersionDetailDto>(v);
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 120)
            return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Invalid, "A name of up to 120 characters is required.");
        if ((req.ChangeSummary ?? "").Length > 4000)
            return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Invalid, "Change summary is limited to 4,000 characters.");
        var issues = MethodologyValidator.Validate(req.Rules, await DefinitionsAsync(ct));
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
            return MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.Invalid, "The rules are not valid.", issues);

        var rules = req.Rules with { SchemaVersion = MethodologyRules.CurrentSchemaVersion };
        var before = new VersionSnapshot(v.Name, v.ChangeSummary, RulesOf(v));
        var hashBefore = v.RulesHash;
        var revisionBefore = v.Revision;
        var now = clock.GetUtcNow();
        v.Name = req.Name.Trim();
        v.ChangeSummary = string.IsNullOrWhiteSpace(req.ChangeSummary) ? null : req.ChangeSummary.Trim();
        v.RulesJson = MethodologyJson.Serialize(rules);
        v.RulesHash = rules.ComputeHash();     // any stored preview is now stale (hash differs) → must preview again
        v.UpdatedAtUtc = now;
        v.UpdatedByUserId = actor.UserId;
        v.UpdatedByEmail = actor.Email;
        v.Revision = revisionBefore + 1;
        var changes = RuleDiff.Compare(before.Rules, rules, await DefinitionsAsync(ct));
        if (before.Name != v.Name) changes.Insert(0, new("(name)", "Version name", RuleChangeKind.Modified, [$"“{before.Name}” → “{v.Name}”"]));
        if (before.ChangeSummary != v.ChangeSummary) changes.Insert(0, new("(summary)", "Change summary", RuleChangeKind.Modified, ["Edited"]));
        store.AddChange(new MethodologyChange
        {
            VersionId = v.Id, VersionName = v.Name, Action = MethodologyChangeAction.SaveDraft, AtUtc = now,
            ActorUserId = actor.UserId, ActorEmail = actor.Email, RevisionBefore = revisionBefore, RevisionAfter = v.Revision,
            RulesHashBefore = hashBefore, RulesHashAfter = v.RulesHash,
            ChangesJson = JsonSerializer.Serialize(changes, MethodologyJson.Options),
            BeforeJson = JsonSerializer.Serialize(before, MethodologyJson.Options),
            AfterJson = JsonSerializer.Serialize(new VersionSnapshot(v.Name, v.ChangeSummary, rules), MethodologyJson.Options),
        });
        try
        {
            // The revision is a concurrency token: the UPDATE only matches if nobody saved in between.
            await store.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            var latest = await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == id), ct);
            return latest is null
                ? MethodologyOutcome<VersionDetailDto>.Fail(MethodologyError.NotFound, MethodologyCodes.DraftConflict, "This draft was deleted by someone else. Reload the page.")
                : DraftConflict<VersionDetailDto>(latest);
        }
        return MethodologyOutcome<VersionDetailDto>.Ok((await GetVersionAsync(id, ct))!);
    }

    private static bool IsConcurrencyConflict(Exception ex) => ex.GetType().Name == "DbUpdateConcurrencyException";

    private static MethodologyOutcome<T> DraftConflict<T>(MethodologyVersion latest) =>
        MethodologyOutcome<T>.Fail(MethodologyError.Conflict, MethodologyCodes.DraftConflict,
            $"This draft was changed by {latest.UpdatedByEmail ?? "someone else"} at {latest.UpdatedAtUtc:yyyy-MM-dd HH:mm} UTC " +
            $"(revision {latest.Revision}) after you opened it. Your changes were NOT saved. Reload the draft to see the latest version, then re-apply your edits.");

    public async Task<MethodologyOutcome<bool>> DeleteDraftAsync(int id, int? expectedRevision, MethodologyActor actor, CancellationToken ct = default)
    {
        if (expectedRevision is null)
            return MethodologyOutcome<bool>.Fail(MethodologyError.Invalid, "revision (the draft revision you are deleting) is required.");
        var v = await store.FindVersionAsync(id, ct);
        if (v is null) return MethodologyOutcome<bool>.Fail(MethodologyError.NotFound, "Version not found.");
        if (v.Status != MethodologyVersionStatus.Draft || v.IsBaseline)
            return MethodologyOutcome<bool>.Fail(MethodologyError.Conflict, "Published versions are kept permanently for history and rollback.");
        if (v.Revision != expectedRevision) return DraftConflict<bool>(v);
        store.AddChange(new MethodologyChange
        {
            VersionId = v.Id, VersionName = v.Name, Action = MethodologyChangeAction.DeleteDraft, AtUtc = clock.GetUtcNow(),
            ActorUserId = actor.UserId, ActorEmail = actor.Email, RevisionBefore = v.Revision, RulesHashBefore = v.RulesHash,
            BeforeJson = JsonSerializer.Serialize(new VersionSnapshot(v.Name, v.ChangeSummary, RulesOf(v)), MethodologyJson.Options),
        });
        try { await store.DeleteVersionAsync(v, ct); }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return MethodologyOutcome<bool>.Fail(MethodologyError.Conflict, MethodologyCodes.DraftConflict,
                "This draft was changed or deleted by someone else at the same time. Reload the page.");
        }
        return MethodologyOutcome<bool>.Ok(true);
    }

    private static MethodologyOutcome<T> Busy<T>() =>
        MethodologyOutcome<T>.Fail(MethodologyError.Conflict, MethodologyCodes.ScoringBusy,
            "Another scoring recalculation is running. Try again when it has finished (usually under a minute).");

    // =============================================================================================================
    // Preview + publish (admin)
    // =============================================================================================================

    /// <summary>
    /// Validates, scores all sites with the version's rules, re-ranks every segment and stores the proposed scores
    /// (never the live ones), returning a comparison against the active methodology.
    /// </summary>
    public async Task<MethodologyOutcome<PreviewSummaryDto>> PreviewAsync(int id, int topN, MethodologyActor actor, CancellationToken ct = default)
    {
        using var lease = gate.TryEnter();
        if (lease is null) return Busy<PreviewSummaryDto>();
        topN = Math.Clamp(topN, 1, MaxTopN);
        var v = await store.FindVersionAsync(id, ct);
        if (v is null) return MethodologyOutcome<PreviewSummaryDto>.Fail(MethodologyError.NotFound, "Version not found.");
        var rules = RulesOf(v);
        var catalog = await ScoringCatalogAsync(ct);
        var issues = MethodologyValidator.Validate(rules, catalog);
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
            return MethodologyOutcome<PreviewSummaryDto>.Fail(MethodologyError.Invalid, "The rules are not valid.", issues);

        var sw = Stopwatch.StartNew();
        var state = (await store.GetStateAsync(ct))!;
        var sites = await store.LoadSitesForScoringAsync(ct);
        var tLoad = sw.ElapsedMilliseconds;
        var proposed = MethodologyEngine.Evaluate(sites, rules, catalog);

        var baselineVersion = await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.IsBaseline), ct);
        var baseline = baselineVersion is null ? null
            : baselineVersion.RulesHash == v.RulesHash ? proposed : MethodologyEngine.Evaluate(sites, RulesOf(baselineVersion), catalog);

        var active = cache.Current;
        var summary = BuildSummary(v, rules, catalog, sites, proposed, baseline, active, state.DataVersion, topN,
            issues.Where(i => i.Severity == IssueSeverity.Warning).ToList(), sw);
        var json = JsonSerializer.Serialize(summary, MethodologyJson.Options);
        var tScore = sw.ElapsedMilliseconds;

        var stored = await store.StoreScoresAsync(v.Id, MethodologyScoring.ToRows(v.Id, rules, proposed), v.RulesHash,
            state.DataVersion, json, clock.GetUtcNow(), ct);
        logger.LogInformation("Preview v{Id}: {Sites} sites — load {Load} ms, score+compare {Score} ms, store {Store} ms",
            v.Id, sites.Count, tLoad, tScore - tLoad, sw.ElapsedMilliseconds - tScore);
        if (stored == ActivationFailure.None)
        {
            store.AddChange(new MethodologyChange
            {
                VersionId = v.Id, VersionName = v.Name, Action = MethodologyChangeAction.Preview, AtUtc = clock.GetUtcNow(),
                ActorUserId = actor.UserId, ActorEmail = actor.Email, RevisionBefore = v.Revision, RevisionAfter = v.Revision,
                RulesHashAfter = v.RulesHash,
                Note = $"Reviewed results for {sites.Count:N0} sites: {summary.ScoresChanged:N0} scores and {summary.RanksChanged:N0} ranks would change."
            });
            await store.SaveChangesAsync(ct);
            await PruneAsync(state, v.Id, ct);
        }
        return stored switch
        {
            ActivationFailure.None => MethodologyOutcome<PreviewSummaryDto>.Ok(summary with { ElapsedMs = sw.ElapsedMilliseconds }),
            ActivationFailure.ConcurrentOperation => MethodologyOutcome<PreviewSummaryDto>.Fail(MethodologyError.Conflict, "Another scoring change, results review or import is running. Try again shortly."),
            ActivationFailure.NotFound => MethodologyOutcome<PreviewSummaryDto>.Fail(MethodologyError.NotFound, "Version not found."),
            _ => MethodologyOutcome<PreviewSummaryDto>.Fail(MethodologyError.Conflict, "The rules or the data changed while the results were being reviewed. Review Results again.")
        };
    }

    /// <summary>
    /// Atomically activates a previewed draft (publish) or a previous version (restore). Requires explicit
    /// confirmation, the active version the preview was compared against, and the previewed rules hash.
    /// </summary>
    public async Task<MethodologyOutcome<PublishResultDto>> PublishAsync(int id, PublishRequest req, MethodologyActor actor, CancellationToken ct = default)
    {
        if (!req.Confirm)
            return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Invalid, "Applying scoring recalculates every site; explicit confirmation is required.");
        if (req.ExpectedActiveVersionId is null || string.IsNullOrWhiteSpace(req.RulesHash))
            return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Invalid, "expectedActiveVersionId and rulesHash (from the reviewed results) are required.");
        if ((req.Note ?? "").Length > 1000)
            return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Invalid, "Note is limited to 1,000 characters.");

        var v = await store.FindVersionAsync(id, ct);
        if (v is null) return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.NotFound, "Version not found.");
        var state = (await store.GetStateAsync(ct))!;
        if (state.ActiveVersionId == id)
            return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, "This version is already active.");
        var issues = MethodologyValidator.Validate(RulesOf(v), await DefinitionsAsync(ct));
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
            return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Invalid, "The rules are not valid.", issues);

        using var lease = gate.TryEnter();
        if (lease is null) return Busy<PublishResultDto>();

        var sw = Stopwatch.StartNew();
        var reason = v.Status == MethodologyVersionStatus.Published ? ActivationReason.Restore : ActivationReason.Publish;
        var now = clock.GetUtcNow();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        var current = cache.Current is { } c && c.VersionId == state.ActiveVersionId ? c : null;
        // Full previous (live) and new methodology for the audit trail.
        var previousVersion = state.ActiveVersionId is { } pid
            ? await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.Id == pid), ct) : null;
        var changes = current is null ? [] : RuleDiff.Compare(current.Rules, RulesOf(v), await DefinitionsAsync(ct));
        var result = await store.ActivateAsync(new ActivateCommand(id, req.ExpectedActiveVersionId, req.RulesHash, reason, now, note)
        {
            ActorUserId = actor.UserId, ActorEmail = actor.Email,
            Audit = new MethodologyChange
            {
                VersionId = v.Id, VersionName = v.Name, AtUtc = now, ActorUserId = actor.UserId, ActorEmail = actor.Email,
                Action = reason == ActivationReason.Restore ? MethodologyChangeAction.Restore : MethodologyChangeAction.Publish,
                RevisionBefore = v.Revision, RevisionAfter = v.Revision, RulesHashBefore = current?.RulesHash, RulesHashAfter = v.RulesHash,
                PreviousActiveVersionId = state.ActiveVersionId, ChangesJson = JsonSerializer.Serialize(changes, MethodologyJson.Options), Note = note,
                BeforeJson = previousVersion is null ? null
                    : JsonSerializer.Serialize(new VersionSnapshot(previousVersion.Name, previousVersion.ChangeSummary, RulesOf(previousVersion)), MethodologyJson.Options),
                AfterJson = JsonSerializer.Serialize(new VersionSnapshot(v.Name, v.ChangeSummary, RulesOf(v)), MethodologyJson.Options),
            },
        }, ct);
        if (!result.Ok)
            return result.Failure switch
            {
                ActivationFailure.NotFound => MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.NotFound, result.Message!),
                ActivationFailure.Failed => MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, MethodologyCodes.Failed, result.Message!),
                _ => MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, result.Message!),
            };

        await AfterScoresChangedAsync(ct);
        await PruneAsync(await store.GetStateAsync(ct), null, ct);
        sw.Stop();
        logger.LogInformation("Methodology version {Id} activated ({Reason}) by {User}; {Sites} sites updated in {Ms} ms",
            id, reason, actor.UserId ?? "(local)", result.SitesUpdated, sw.ElapsedMilliseconds);
        return MethodologyOutcome<PublishResultDto>.Ok(new PublishResultDto(id, result.PreviousVersionId, result.SitesUpdated, sw.ElapsedMilliseconds));
    }

    /// <summary>Re-computes the ACTIVE methodology over the current data and re-materialises it (admin "rescore").</summary>
    public async Task<MethodologyOutcome<PublishResultDto>> ReapplyActiveAsync(ActivationReason reason = ActivationReason.Rescore, CancellationToken ct = default)
    {
        var state = await store.GetStateAsync(ct);
        if (state?.ActiveVersionId is not { } id) return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, "No active methodology.");
        using var lease = gate.TryEnter();
        if (lease is null) return Busy<PublishResultDto>();
        var v = (await store.FindVersionAsync(id, ct))!;
        var rules = RulesOf(v);
        var sw = Stopwatch.StartNew();
        var sites = await store.LoadSitesForScoringAsync(ct);
        var result = MethodologyEngine.Evaluate(sites, rules, await ScoringCatalogAsync(ct));
        var stored = await store.StoreScoresAsync(id, MethodologyScoring.ToRows(id, rules, result), v.RulesHash, state.DataVersion, v.PreviewSummaryJson, clock.GetUtcNow(), ct);
        if (stored != ActivationFailure.None) return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, $"Could not store scores ({stored}).");
        var act = await store.ActivateAsync(new ActivateCommand(id, id, v.RulesHash, reason, clock.GetUtcNow(), "Active methodology re-applied to the current data."), ct);
        if (!act.Ok) return MethodologyOutcome<PublishResultDto>.Fail(MethodologyError.Conflict, act.Message!);
        await AfterScoresChangedAsync(ct);
        return MethodologyOutcome<PublishResultDto>.Ok(new PublishResultDto(id, id, act.SitesUpdated, sw.ElapsedMilliseconds));
    }

    /// <summary>
    /// Keeps stored proposed scores only for the baseline, active, previous-active and <paramref name="justReviewed"/>
    /// versions plus the <see cref="KeepReviewedVersions"/> most recently reviewed others. Best effort: a failure is
    /// logged and never fails the request (the scores were already saved).
    /// </summary>
    private async Task PruneAsync(MethodologyState? state, int? justReviewed, CancellationToken ct)
    {
        try
        {
            var versions = await read.ToListAsync(read.MethodologyVersions
                .Select(x => new { x.Id, x.IsBaseline, x.PreviewRulesHash, x.PreviewedAtUtc }), ct);
            var keep = new HashSet<int>(versions.Where(x => x.IsBaseline).Select(x => x.Id));
            if (state?.ActiveVersionId is { } a) keep.Add(a);
            if (state?.PreviousActiveVersionId is { } p) keep.Add(p);
            if (justReviewed is { } j) keep.Add(j);
            foreach (var x in versions.Where(x => x.PreviewRulesHash is not null && !keep.Contains(x.Id))
                         .OrderByDescending(x => x.PreviewedAtUtc).Take(KeepReviewedVersions))
                keep.Add(x.Id);
            if (!versions.Any(x => x.PreviewRulesHash is not null && !keep.Contains(x.Id))) return;
            var pruned = await store.PruneStoredScoresAsync(keep, ct);
            if (pruned > 0) logger.LogInformation("Pruned stored proposed scores of {Count} older reviewed version(s)", pruned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pruning stored proposed scores failed (non-fatal)");
        }
    }

    private async Task AfterScoresChangedAsync(CancellationToken ct)
    {
        await RefreshCacheAsync(ct);
        // Every Score/Rank changed: refresh optimizer statistics for the score-ordered dashboard queries.
        try { await write.UpdateScoreStatisticsAsync(ct); }
        catch (Exception ex) { logger.LogWarning(ex, "UPDATE STATISTICS failed (non-fatal)"); }
    }

    // =============================================================================================================
    // Helpers
    // =============================================================================================================

    public static MethodologyRules RulesOf(MethodologyVersion v) => MethodologyJson.Deserialize(v.RulesJson);

    private async Task<VersionSummaryDto> SummaryAsync(MethodologyVersion v, MethodologyState? state, CancellationToken ct)
    {
        var baselineHash = v.IsBaseline ? v.RulesHash
            : (await read.FirstOrDefaultAsync(read.MethodologyVersions.Where(x => x.IsBaseline).Select(x => x.RulesHash), ct));
        return Summary(v, state, baselineHash);
    }

    private static VersionSummaryDto Summary(MethodologyVersion v, MethodologyState? state, string? baselineHash)
    {
        var rules = RulesOf(v);
        return new VersionSummaryDto(v.Id, v.Name, v.Status, v.IsBaseline, state?.ActiveVersionId == v.Id,
            state?.PreviousActiveVersionId == v.Id, v.RulesHash, v.ChangeSummary, v.BasedOnVersionId,
            v.CreatedAtUtc, v.UpdatedAtUtc, v.PublishedAtUtc, v.PreviewedAtUtc,
            v.PreviewRulesHash == v.RulesHash && v.ScoresDataVersion == state?.DataVersion,
            rules.Criteria.Count, rules.Criteria.Count(c => c.Enabled), v.RulesHash == baselineHash)
        {
            Revision = v.Revision, CreatedByEmail = v.CreatedByEmail, UpdatedByEmail = v.UpdatedByEmail, PublishedByEmail = v.PublishedByEmail,
        };
    }

    public static List<CriterionViewDto> Views(MethodologyRules rules, FieldCatalog? catalog = null) =>
        rules.Criteria.Select(c => View(c, catalog)).ToList();

    public static CriterionViewDto View(Criterion c, FieldCatalog? catalog = null)
    {
        var (min, max) = MethodologyEngine.PossibleRange(new MethodologyRules { Criteria = [c with { Enabled = true }] }, catalog);
        var missable = MethodologyEngine.UsesMissableField(c, catalog);
        return new CriterionViewDto(c.Key, c.Name, CriterionText.CategoryLabel(c.Category), c.Enabled, c.BuiltIn, c.Kind,
            CriterionText.Rule(c, catalog), c.Explanation, min, max, MethodologyEngine.FieldsUsed(c).ToList(), missable,
            missable ? MissingPolicyText(c) : null);
    }

    private static string MissingPolicyText(Criterion c) => c.Missing.Mode == MissingDataMode.FixedPoints
        ? $"Sites without data get {CriterionText.Pts(c.Missing.Points)} points"
        : "Sites without data get 0 points";

    private static FieldDto FieldStats(ScoringField f, List<CandidateSite> sites)
    {
        int present = 0, missing = 0, zero = 0;
        double? min = null, max = null;
        Dictionary<string, int>? cats = f.Type is FieldType.Category or FieldType.LotCategory ? new(StringComparer.OrdinalIgnoreCase) : null;
        foreach (var s in sites)
        {
            var v = f.Read(s);
            if (v.IsMissing) { missing++; continue; }
            present++;
            if (v.Number is { } n)
            {
                if (n == 0) zero++;
                min = min is null ? n : Math.Min(min.Value, n);
                max = max is null ? n : Math.Max(max.Value, n);
            }
            if (cats is not null)
                foreach (var c in v.Lots ?? [v.Text!]) cats[c] = cats.GetValueOrDefault(c) + 1;
        }
        if (f.Type == FieldType.Boolean)
        {
            var yes = sites.Count(s => f.Read(s).Flag == true);
            cats = new Dictionary<string, int> { ["yes"] = yes, ["no"] = present - yes };
        }
        return new FieldDto(f.Key, f.Label, f.Type, f.Unit, f.Group, f.Description, f.MissingMeaning, f.Categories,
            ScoringFieldCatalog.OperatorsFor(f.Type), present, missing, zero, min, max, cats);
    }

    private static PreviewSummaryDto BuildSummary(MethodologyVersion v, MethodologyRules rules, FieldCatalog catalog, List<CandidateSite> sites,
        MethodologyResult proposed, MethodologyResult? baseline, ActiveMethodology? active, int dataVersion, int topN,
        List<ValidationIssue> warnings, Stopwatch sw)
    {
        var n = sites.Count;
        MoverDto Mover(int i) => new(sites[i].SiteId, sites[i].SiteAddress, sites[i].Municipality, sites[i].Segment,
            sites[i].Score, sites[i].ComputedRank, proposed.Sites[i].Score, proposed.Sites[i].Rank,
            baseline?.Sites[i].Score, baseline?.Sites[i].Rank);

        int scoresChanged = 0, ranksChanged = 0;
        double maxUp = 0, maxDown = 0, absSum = 0;
        for (var i = 0; i < n; i++)
        {
            var d = Math.Round(proposed.Sites[i].Score - sites[i].Score, 1);
            if (d != 0) scoresChanged++;
            if (proposed.Sites[i].Rank != sites[i].ComputedRank) ranksChanged++;
            maxUp = Math.Max(maxUp, d); maxDown = Math.Min(maxDown, d); absSum += Math.Abs(d);
        }

        var segments = Enumerable.Range(0, n).GroupBy(i => sites[i].Segment).OrderBy(g => g.Key).Select(g =>
        {
            var idx = g.ToList();
            var activeTop = idx.Where(i => sites[i].ComputedRank <= topN).ToHashSet();
            var proposedTop = idx.Where(i => proposed.Sites[i].Rank <= topN).OrderBy(i => proposed.Sites[i].Rank).ToList();
            return new SegmentPreviewDto(g.Key, idx.Count,
                Math.Round(idx.Average(i => sites[i].Score), 1), Math.Round(idx.Average(i => proposed.Sites[i].Score), 1),
                idx.Count(i => proposed.Sites[i].Rank != sites[i].ComputedRank),
                proposedTop.Count(activeTop.Contains),
                proposedTop.Where(i => !activeTop.Contains(i)).Select(Mover).ToList(),
                activeTop.Where(i => proposed.Sites[i].Rank > topN).OrderBy(i => sites[i].ComputedRank).Select(Mover).ToList(),
                proposedTop.Select(Mover).ToList());
        }).ToList();

        var risers = Enumerable.Range(0, n).Where(i => sites[i].ComputedRank > proposed.Sites[i].Rank)
            .OrderByDescending(i => sites[i].ComputedRank - proposed.Sites[i].Rank).ThenBy(i => proposed.Sites[i].Rank).Take(15).Select(Mover).ToList();
        var fallers = Enumerable.Range(0, n).Where(i => sites[i].ComputedRank < proposed.Sites[i].Rank)
            .OrderByDescending(i => proposed.Sites[i].Rank - sites[i].ComputedRank).ThenBy(i => sites[i].ComputedRank).Take(15).Select(Mover).ToList();

        var criteria = rules.Criteria.Select((c, k) =>
        {
            var pts = proposed.Sites.Select(s => s.Points[k]).ToList();
            return new CriterionImpactDto(c.Key, c.Name, c.Enabled,
                n == 0 ? 0 : Math.Round(pts.Average(), 2), n == 0 ? 0 : Math.Round(pts.Min(), 2), n == 0 ? 0 : Math.Round(pts.Max(), 2),
                pts.Count(p => p != 0), proposed.Sites.Count(s => s.Missing[k]),
                MethodologyEngine.UsesMissableField(c, catalog) ? MissingPolicyText(c) : null);
        }).ToList();

        return new PreviewSummaryDto(v.Id, v.Name, v.RulesHash, dataVersion, DateTimeOffset.UtcNow, sw.ElapsedMilliseconds, n,
            active?.VersionId ?? 0, active?.Name ?? "(none)", active?.RulesHash == v.RulesHash,
            scoresChanged, ranksChanged, Math.Round(maxUp, 1), Math.Round(maxDown, 1), n == 0 ? 0 : Math.Round(absSum / n, 2),
            topN, segments, risers, fallers, criteria,
            active is null ? [] : RuleDiff.Compare(active.Rules, rules, catalog), warnings);
    }
}

/// <summary>Field coverage statistics, recomputed only when the data version or site count changes.</summary>
public sealed class FieldStatsCache
{
    private readonly Lock _gate = new();
    private (int, int) _key = (-1, -1);
    private IReadOnlyList<FieldDto> _value = [];

    public bool TryGet((int DataVersion, int Sites) key, out IReadOnlyList<FieldDto> value)
    {
        lock (_gate) { value = _value; return _key == key; }
    }

    public void Set((int DataVersion, int Sites) key, IReadOnlyList<FieldDto> value)
    {
        lock (_gate) { _key = key; _value = value; }
    }
}
