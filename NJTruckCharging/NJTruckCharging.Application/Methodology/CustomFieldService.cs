using System.Security.Cryptography;
using System.Text;
using NJTruckCharging.Application.Abstractions;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Application.Methodology;

public sealed record CustomFieldDto(
    string Key, string Label, string? Description, CustomFieldType Type, CustomFieldSource Source, string? Unit,
    double? MinValue, double? MaxValue, IReadOnlyList<string> Categories, int ValueCount, int SiteCount,
    bool UsedByActive, IReadOnlyList<int> UsedByVersions, DateTimeOffset CreatedAtUtc);

public sealed record CustomFieldRequest(
    string Label, string? Description, CustomFieldType Type, CustomFieldSource Source, string? Unit,
    double? MinValue, double? MaxValue, IReadOnlyList<string>? Categories);

public sealed record CustomValueDto(
    string FieldKey, string Label, CustomFieldType Type, CustomFieldSource Source, string? Unit, IReadOnlyList<string> Categories,
    double? MinValue, double? MaxValue, string? Description,
    /// <summary>"Unknown" when no value has been recorded/imported.</summary>
    string Status, string? Display, double? NumberValue, bool? BoolValue, string? CategoryValue,
    string? Evidence, string? ImportBatch, DateTimeOffset? UpdatedAtUtc, bool UsedByActive);

public sealed record SetCustomValueRequest(string Value, string? Evidence);

public sealed record ImportIssue(int Line, string? SiteId, string Message);

public sealed record ImportPreviewDto(
    string FieldKey, string FileName, int Rows, int ValidRows, int Inserts, int Updates, int Unchanged,
    int UnknownSiteIds, int Duplicates, int InvalidValues, IReadOnlyList<ImportIssue> Errors,
    IReadOnlyList<ImportSampleRow> Sample, string Token, bool CanCommit, int SitesWithoutValueAfter);

public sealed record ImportSampleRow(int Line, string SiteId, string Value, string? Current, string Change);

public sealed record ImportRequest(string FileName, string Csv, string? Token);

public sealed record ImportResultDto2(int Inserted, int Updated, string Batch);

/// <summary>
/// User-defined custom data fields: definitions, manual per-site values with source evidence, and dataset imports
/// mapped by stable Site ID (parse → validate → preview → commit). Values never change live scores by themselves —
/// they take effect at the next preview/publish (or re-score) of a methodology that uses them.
/// </summary>
public sealed class CustomFieldService(ICustomFieldStore store, IReadStore read, ActiveMethodologyCache active, TimeProvider clock)
{
    public const int MaxImportBytes = 5_000_000;
    public const int MaxImportRows = 50_000;

    public async Task<List<CustomFieldDto>> ListAsync(CancellationToken ct = default)
    {
        var fields = await store.ListFieldsAsync(ct);
        var counts = await store.CountsByFieldAsync(ct);
        var sites = await read.CountAsync(read.Sites, ct);
        var usage = await UsageAsync(ct);
        return fields.Select(f => ToDto(f, counts.GetValueOrDefault(f.Key), sites, usage)).ToList();
    }

    public async Task<MethodologyOutcome<CustomFieldDto>> CreateAsync(CustomFieldRequest req, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var existing = (await store.ListFieldsAsync(ct)).Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseKey = CustomFields.KeyFromLabel(req.Label ?? "");
        var key = baseKey;
        for (var n = 2; existing.Contains(key) || ScoringFieldCatalog.Find(key) is not null; n++)
            key = (baseKey.Length > 36 ? baseKey[..36] : baseKey) + n;

        var f = new CustomDataField
        {
            Key = key, Label = (req.Label ?? "").Trim(), Description = Clean(req.Description), Type = req.Type, Source = req.Source,
            Unit = req.Type == CustomFieldType.Number ? Clean(req.Unit) : null,
            MinValue = req.Type == CustomFieldType.Number ? req.MinValue : null,
            MaxValue = req.Type == CustomFieldType.Number ? req.MaxValue : null,
            Categories = req.Type == CustomFieldType.Category ? string.Join('|', (req.Categories ?? []).Select(c => c.Trim()).Where(c => c.Length > 0)) : null,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        if ((req.Categories ?? []).Any(c => c.Contains('|')))
            return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.Invalid, "Categories cannot contain '|'.");
        var errors = CustomFields.ValidateDefinition(f);
        if (errors.Count > 0) return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.Invalid, string.Join(" ", errors));
        await store.AddFieldAsync(f, ct);
        return MethodologyOutcome<CustomFieldDto>.Ok(ToDto(f, 0, await read.CountAsync(read.Sites, ct), await UsageAsync(ct)));
    }

    /// <summary>Name/description/unit can change any time; type and source are fixed once created (values depend on them).</summary>
    public async Task<MethodologyOutcome<CustomFieldDto>> UpdateAsync(string key, CustomFieldRequest req, CancellationToken ct = default)
    {
        var f = await store.FindFieldAsync(key, ct);
        if (f is null) return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.NotFound, "Custom field not found.");
        if (req.Type != f.Type || req.Source != f.Source)
            return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.Conflict, "Data type and value source cannot change after creation — create a new field instead.");
        var values = await store.ValuesForFieldAsync(key, ct);
        var cats = (req.Categories ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        if (f.Type == CustomFieldType.Category)
        {
            var removed = f.CategoryList.Where(c => !cats.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            var inUse = removed.Where(c => values.Any(v => string.Equals(v.CategoryValue, c, StringComparison.OrdinalIgnoreCase))).ToList();
            if (inUse.Count > 0)
                return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.Conflict, $"Categories still used by recorded values cannot be removed: {string.Join(", ", inUse)}.");
        }
        var copy = new CustomDataField
        {
            Key = f.Key, Label = (req.Label ?? "").Trim(), Description = Clean(req.Description), Type = f.Type, Source = f.Source,
            Unit = f.Type == CustomFieldType.Number ? Clean(req.Unit) : null,
            MinValue = f.Type == CustomFieldType.Number ? req.MinValue : null, MaxValue = f.Type == CustomFieldType.Number ? req.MaxValue : null,
            Categories = f.Type == CustomFieldType.Category ? string.Join('|', cats) : null
        };
        var errors = CustomFields.ValidateDefinition(copy);
        if (copy.Type == CustomFieldType.Number && values.Any(v => v.NumberValue is { } n && (n < copy.MinValue || n > copy.MaxValue)))
            errors.Add("Recorded values fall outside the new bounds.");
        if (errors.Count > 0) return MethodologyOutcome<CustomFieldDto>.Fail(MethodologyError.Invalid, string.Join(" ", errors));
        f.Label = copy.Label; f.Description = copy.Description; f.Unit = copy.Unit; f.MinValue = copy.MinValue; f.MaxValue = copy.MaxValue;
        f.Categories = copy.Categories; f.UpdatedAtUtc = clock.GetUtcNow();
        await store.SaveChangesAsync(ct);
        return MethodologyOutcome<CustomFieldDto>.Ok(ToDto(f, values.Count, await read.CountAsync(read.Sites, ct), await UsageAsync(ct)));
    }

    // ---- per-site values (Site Details) ---------------------------------------------------------------------------

    public async Task<List<CustomValueDto>> ValuesForSiteAsync(string siteId, CancellationToken ct = default)
    {
        var fields = await store.ListFieldsAsync(ct);
        var values = (await store.ValuesForSiteAsync(siteId, ct)).ToDictionary(v => v.FieldKey);
        var activeKeys = ActiveFieldKeys();
        return fields.Select(f =>
        {
            var v = values.GetValueOrDefault(f.Key);
            return new CustomValueDto(f.Key, f.Label, f.Type, f.Source, f.Unit, f.CategoryList, f.MinValue, f.MaxValue, f.Description,
                v is null ? "Unknown" : "Recorded", v is null ? null : CustomFields.Display(f, v),
                v?.NumberValue, v?.BoolValue, v?.CategoryValue, v?.Evidence, v?.ImportBatch, v?.UpdatedAtUtc, activeKeys.Contains(f.Key));
        }).ToList();
    }

    /// <summary>Records a manual value. The value must parse for the field's type/bounds and evidence is required.</summary>
    public async Task<MethodologyOutcome<CustomValueDto>> SetManualAsync(string key, string siteId, SetCustomValueRequest req, CancellationToken ct = default)
    {
        var f = await store.FindFieldAsync(key, ct);
        if (f is null) return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.NotFound, "Custom field not found.");
        if (f.Source != CustomFieldSource.Manual)
            return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.Conflict, "This field's values come from a dataset import, not manual entry.");
        if (!(await store.SiteIdsAsync(ct)).Contains(siteId))
            return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.NotFound, $"No candidate site with Site ID '{siteId}'.");
        var parsed = CustomFields.Parse(f, req.Value);
        if (!parsed.Ok) return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.Invalid, parsed.Error!);
        var evidence = Clean(req.Evidence);
        if (evidence is null) return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.Invalid, "Source evidence is required (document, call, site visit, link…).");
        if (evidence.Length > CustomFields.MaxEvidenceLength)
            return MethodologyOutcome<CustomValueDto>.Fail(MethodologyError.Invalid, $"Evidence is limited to {CustomFields.MaxEvidenceLength} characters.");

        var now = clock.GetUtcNow();
        var v = await store.FindValueAsync(key, siteId, ct);
        if (v is null)
        {
            v = new CustomFieldValue { FieldKey = key, SiteId = siteId, CreatedAtUtc = now };
            Apply(v, parsed, evidence, now);
            await store.AddValueAsync(v, ct);
        }
        else
        {
            Apply(v, parsed, evidence, now);
            await store.SaveChangesAsync(ct);
        }
        return MethodologyOutcome<CustomValueDto>.Ok((await ValuesForSiteAsync(siteId, ct)).Single(x => x.FieldKey == key));
    }

    /// <summary>Clears a value: the site goes back to Unknown (missing-data policy applies at the next scoring).</summary>
    public async Task<MethodologyOutcome<bool>> ClearAsync(string key, string siteId, CancellationToken ct = default)
    {
        var v = await store.FindValueAsync(key, siteId, ct);
        if (v is null) return MethodologyOutcome<bool>.Fail(MethodologyError.NotFound, "No value recorded.");
        await store.RemoveValueAsync(v, ct);
        return MethodologyOutcome<bool>.Ok(true);
    }

    // ---- dataset import -----------------------------------------------------------------------------------------

    public async Task<MethodologyOutcome<ImportPreviewDto>> PreviewImportAsync(string key, ImportRequest req, CancellationToken ct = default)
    {
        var (field, error) = await ImportFieldAsync(key, ct);
        if (field is null) return MethodologyOutcome<ImportPreviewDto>.Fail(error!.Value.E, error.Value.M);
        if (Encoding.UTF8.GetByteCount(req.Csv ?? "") > MaxImportBytes)
            return MethodologyOutcome<ImportPreviewDto>.Fail(MethodologyError.Invalid, $"The file is larger than {MaxImportBytes / 1_000_000} MB.");
        var plan = await PlanAsync(field, req, ct);
        return plan.Fatal is { } fatal
            ? MethodologyOutcome<ImportPreviewDto>.Fail(MethodologyError.Invalid, fatal)
            : MethodologyOutcome<ImportPreviewDto>.Ok(plan.Preview!);
    }

    /// <summary>Commits exactly the previewed file (token must match) and only if it has no errors.</summary>
    public async Task<MethodologyOutcome<ImportResultDto2>> CommitImportAsync(string key, ImportRequest req, CancellationToken ct = default)
    {
        var (field, error) = await ImportFieldAsync(key, ct);
        if (field is null) return MethodologyOutcome<ImportResultDto2>.Fail(error!.Value.E, error.Value.M);
        var plan = await PlanAsync(field, req, ct);
        if (plan.Fatal is { } fatal) return MethodologyOutcome<ImportResultDto2>.Fail(MethodologyError.Invalid, fatal);
        var p = plan.Preview!;
        if (string.IsNullOrEmpty(req.Token) || req.Token != p.Token)
            return MethodologyOutcome<ImportResultDto2>.Fail(MethodologyError.Conflict, "The file or the current values changed since the preview. Preview the import again.");
        if (!p.CanCommit)
            return MethodologyOutcome<ImportResultDto2>.Fail(MethodologyError.Invalid, $"The file has {p.Errors.Count} error(s); fix them and preview again.");
        var batch = $"{Path.GetFileName(req.FileName ?? "import.csv")} @ {clock.GetUtcNow():yyyy-MM-dd HH:mm} UTC";
        if (batch.Length > 200) batch = batch[^200..];
        var (ins, upd) = await store.UpsertImportAsync(key, plan.Rows, batch, clock.GetUtcNow(), ct);
        return MethodologyOutcome<ImportResultDto2>.Ok(new ImportResultDto2(ins, upd, batch));
    }

    private sealed record Plan(string? Fatal, ImportPreviewDto? Preview, List<CustomImportRow> Rows);

    private async Task<Plan> PlanAsync(CustomDataField f, ImportRequest req, CancellationToken ct)
    {
        var table = Csv.Parse(req.Csv ?? "");
        if (table.Count == 0) return new("The file is empty.", null, []);
        var header = table[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var siteCol = header.FindIndex(h => h is "site_id" or "siteid" or "site id");
        var valueCol = header.FindIndex(h => h == "value" || h == f.Key.ToLowerInvariant() || h == f.Label.Trim().ToLowerInvariant());
        var evidenceCol = header.FindIndex(h => h is "evidence" or "source");
        if (siteCol < 0 || valueCol < 0)
            return new($"The first row must be a header with a 'site_id' column and a 'value' (or '{f.Label}') column; optional 'evidence'.", null, []);
        if (table.Count - 1 > MaxImportRows) return new($"At most {MaxImportRows:N0} data rows per import.", null, []);

        var siteIds = await store.SiteIdsAsync(ct);
        var current = (await store.ValuesForFieldAsync(f.Key, ct)).ToDictionary(v => v.SiteId, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportIssue>();
        var rows = new List<CustomImportRow>();
        var sample = new List<ImportSampleRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unknown = 0, dupes = 0, invalid = 0, inserts = 0, updates = 0, unchanged = 0;
        for (var i = 1; i < table.Count; i++)
        {
            var r = table[i];
            if (r.All(c => string.IsNullOrWhiteSpace(c))) continue;          // blank line
            var line = i + 1;
            var sid = Cell(r, siteCol).Trim();
            var raw = Cell(r, valueCol);
            var evidence = evidenceCol >= 0 ? Clean(Cell(r, evidenceCol)) : null;
            if (sid.Length == 0) { errors.Add(new(line, null, "Missing site_id.")); invalid++; continue; }
            if (!siteIds.Contains(sid)) { errors.Add(new(line, sid, "Unknown Site ID (not in the candidate dataset).")); unknown++; continue; }
            if (!seen.Add(sid)) { errors.Add(new(line, sid, "Duplicate Site ID in the file.")); dupes++; continue; }
            var parsed = CustomFields.Parse(f, raw);
            if (!parsed.Ok) { errors.Add(new(line, sid, parsed.Error!)); invalid++; continue; }
            if (evidence is { Length: > CustomFields.MaxEvidenceLength }) { errors.Add(new(line, sid, "Evidence is too long.")); invalid++; continue; }
            var cur = current.GetValueOrDefault(sid);
            var curDisplay = cur is null ? null : CustomFields.Display(f, cur);
            var same = cur is not null && cur.NumberValue == parsed.Number && cur.BoolValue == parsed.Flag
                       && string.Equals(cur.CategoryValue, parsed.Category, StringComparison.Ordinal) && cur.Evidence == evidence;
            var change = cur is null ? "new" : same ? "unchanged" : "update";
            if (cur is null) inserts++; else if (same) unchanged++; else updates++;
            rows.Add(new CustomImportRow(sid, parsed.Number, parsed.Flag, parsed.Category, evidence));
            if (sample.Count < 25) sample.Add(new(line, sid, raw.Trim(), curDisplay, change));
        }
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            f.Key + "\n" + req.Csv + "\n" + string.Join(";", current.OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value.NumberValue}|{kv.Value.BoolValue}|{kv.Value.CategoryValue}|{kv.Value.UpdatedAtUtc:O}")))))[..32];
        var withValue = current.Keys.Concat(rows.Select(r => r.SiteId)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var preview = new ImportPreviewDto(f.Key, req.FileName ?? "import.csv", rows.Count + errors.Count, rows.Count, inserts, updates, unchanged,
            unknown, dupes, invalid, errors.Take(200).ToList(), sample, token, errors.Count == 0 && rows.Count > 0,
            Math.Max(0, siteIds.Count - withValue));
        return new(null, preview, rows);
    }

    private async Task<(CustomDataField? Field, (MethodologyError E, string M)? Error)> ImportFieldAsync(string key, CancellationToken ct)
    {
        var f = (await store.ListFieldsAsync(ct)).FirstOrDefault(x => x.Key == key);
        if (f is null) return (null, (MethodologyError.NotFound, "Custom field not found."));
        if (f.Source != CustomFieldSource.Imported) return (null, (MethodologyError.Conflict, "This field is recorded manually per site, not imported."));
        return (f, null);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private HashSet<string> ActiveFieldKeys() =>
        (active.Current?.Rules.Criteria ?? []).Where(c => c.Enabled).SelectMany(MethodologyEngine.FieldsUsed).ToHashSet(StringComparer.Ordinal);

    private async Task<Dictionary<string, List<int>>> UsageAsync(CancellationToken ct)
    {
        var versions = await read.ToListAsync(read.MethodologyVersions.Select(v => new { v.Id, v.RulesJson }), ct);
        var usage = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var v in versions)
            foreach (var key in MethodologyJson.Deserialize(v.RulesJson).Criteria.SelectMany(MethodologyEngine.FieldsUsed).Where(k => k.StartsWith(CustomFields.KeyPrefix)).Distinct())
                (usage.TryGetValue(key, out var l) ? l : usage[key] = []).Add(v.Id);
        return usage;
    }

    private CustomFieldDto ToDto(CustomDataField f, int count, int sites, Dictionary<string, List<int>> usage) =>
        new(f.Key, f.Label, f.Description, f.Type, f.Source, f.Unit, f.MinValue, f.MaxValue, f.CategoryList, count, sites,
            ActiveFieldKeys().Contains(f.Key), usage.GetValueOrDefault(f.Key) ?? [], f.CreatedAtUtc);

    private static void Apply(CustomFieldValue v, ParsedValue p, string? evidence, DateTimeOffset now)
    {
        v.NumberValue = p.Number; v.BoolValue = p.Flag; v.CategoryValue = p.Category;
        v.Evidence = evidence; v.Source = CustomFieldSource.Manual; v.ImportBatch = null; v.UpdatedAtUtc = now;
    }

    private static string Cell(IReadOnlyList<string> r, int i) => i < r.Count ? r[i] : "";
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Minimal RFC-4180 CSV reader (quotes, doubled quotes, CRLF/LF). Cells are returned as plain text only.</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n': row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = []; break;
                default: cell.Append(ch); break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
