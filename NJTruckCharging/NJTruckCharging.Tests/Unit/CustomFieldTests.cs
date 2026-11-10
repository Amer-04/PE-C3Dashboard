using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Domain.Entities;
using NJTruckCharging.Domain.Methodology;

namespace NJTruckCharging.Tests.Unit;

public class CustomFieldTests
{
    private static CustomDataField Num(double? min = null, double? max = null) =>
        new() { Key = "cfCapacityKw", Label = "Available capacity", Type = CustomFieldType.Number, Source = CustomFieldSource.Manual, Unit = "kW", MinValue = min, MaxValue = max };
    private static readonly CustomDataField Flag =
        new() { Key = "cfLetterReceived", Label = "Utility letter received", Type = CustomFieldType.Boolean, Source = CustomFieldSource.Manual };
    private static readonly CustomDataField Cat =
        new() { Key = "cfZoningStatus", Label = "Zoning status", Type = CustomFieldType.Category, Source = CustomFieldSource.Imported, Categories = "Permitted|Conditional|Prohibited" };

    [Theory]
    [InlineData("1500", 1500.0)]
    [InlineData(" 2,500.5 ", 2500.5)]
    [InlineData("-3", -3.0)]
    [InlineData("1e3", 1000.0)]
    public void Number_values_parse_strictly(string raw, double expected)
    {
        var p = CustomFields.Parse(Num(), raw);
        Assert.True(p.Ok);
        Assert.Equal(expected, p.Number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12kW")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1; DROP TABLE CandidateSites")]
    [InlineData("=SUM(A1:A9)")]
    public void Invalid_numbers_are_rejected_never_coerced(string raw) => Assert.False(CustomFields.Parse(Num(), raw).Ok);

    [Fact]
    public void Number_bounds_are_enforced()
    {
        var f = Num(0, 5000);
        Assert.True(CustomFields.Parse(f, "0").Ok);
        Assert.True(CustomFields.Parse(f, "5000").Ok);
        Assert.Contains("below the minimum", CustomFields.Parse(f, "-1").Error);
        Assert.Contains("above the maximum", CustomFields.Parse(f, "5000.1").Error);
    }

    [Theory]
    [InlineData("yes", true)] [InlineData("Y", true)] [InlineData("TRUE", true)] [InlineData("1", true)]
    [InlineData("no", false)] [InlineData("n", false)] [InlineData("false", false)] [InlineData("0", false)]
    public void Booleans_accept_yes_no_forms(string raw, bool expected) => Assert.Equal(expected, CustomFields.Parse(Flag, raw).Flag);

    [Theory]
    [InlineData("maybe")] [InlineData("2")] [InlineData("")]
    public void Invalid_booleans_are_rejected(string raw) => Assert.False(CustomFields.Parse(Flag, raw).Ok);

    [Fact]
    public void Categories_must_be_one_of_the_defined_values_and_are_canonicalised()
    {
        Assert.Equal("Conditional", CustomFields.Parse(Cat, "conditional").Category);
        Assert.Contains("is not one of", CustomFields.Parse(Cat, "Unknown").Error);
    }

    [Fact]
    public void Definitions_are_validated()
    {
        Assert.Empty(CustomFields.ValidateDefinition(Num(0, 10)));
        Assert.NotEmpty(CustomFields.ValidateDefinition(Num(10, 0)));                                     // min > max
        Assert.NotEmpty(CustomFields.ValidateDefinition(new CustomDataField { Key = "cfX1", Label = " ", Type = CustomFieldType.Number }));
        Assert.NotEmpty(CustomFields.ValidateDefinition(new CustomDataField { Key = "cfZ1", Label = "One", Type = CustomFieldType.Category, Categories = "OnlyOne" }));
        Assert.NotEmpty(CustomFields.ValidateDefinition(new CustomDataField { Key = "cfZ2", Label = "Dup", Type = CustomFieldType.Category, Categories = "A|a" }));
        Assert.NotEmpty(CustomFields.ValidateDefinition(new CustomDataField { Key = "milesToExit", Label = "Shadow", Type = CustomFieldType.Number }));
    }

    [Theory]
    [InlineData("Utility capacity letter received", "cfUtilityCapacityLetterReceived")]
    [InlineData("kW available (2026)!", "cfKWAvailable2026")]
    [InlineData("'; DROP TABLE x; --", "cfDROPTABLEX")]
    [InlineData("€€€", "cfField")]
    public void Keys_are_safe_identifiers_derived_from_free_text_names(string label, string expected)
    {
        var key = CustomFields.KeyFromLabel(label);
        Assert.Equal(expected, key);
        Assert.True(CustomFields.IsValidKey(key));
    }

    [Fact]
    public void Csv_parser_handles_quotes_commas_and_newlines()
    {
        var rows = Csv.Parse("﻿site_id,value,evidence\r\nA-1,12,\"Letter, dated \"\"May\"\"\"\nB-2,yes,\"multi\nline\"\n");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["A-1", "12", "Letter, dated \"May\""], rows[1]);
        Assert.Equal("multi\nline", rows[2][2]);
        Assert.Equal("site_id", rows[0][0]);
    }

    [Fact]
    public void Engine_scores_custom_fields_only_through_typed_values_and_applies_the_missing_policy_to_unknown_sites()
    {
        var sites = new List<CandidateSite>
        {
            new() { SiteId = "S1", Segment = 1, MilesToExit = 1, PropClass = "4B" },
            new() { SiteId = "S2", Segment = 1, MilesToExit = 2, PropClass = "4B" },
            new() { SiteId = "S3", Segment = 1, MilesToExit = 3, PropClass = "4B" },
        };
        var f = Num();
        var values = new List<CustomFieldValue>
        {
            new() { FieldKey = f.Key, SiteId = "S1", NumberValue = 2000 },
            new() { FieldKey = f.Key, SiteId = "S2", NumberValue = 500 },
        };
        var catalog = CustomCatalog.Build([f], values);
        var rules = new MethodologyRules
        {
            Criteria =
            [
                new Criterion
                {
                    Key = "capacity", Name = "Anything the user likes to call it", Category = CriterionCategory.Bonus, Kind = CriterionKind.Cases,
                    Cases = [new ScoreCase { Points = 10, Conditions = [new Condition { Field = f.Key, Op = ConditionOp.Gte, Value = 1000 }] }],
                    DefaultPoints = -2,
                    Missing = new MissingDataPolicy { Mode = MissingDataMode.FixedPoints, Points = -5 }
                }
            ]
        };
        Assert.DoesNotContain(MethodologyValidator.Validate(rules, catalog), i => i.Severity == IssueSeverity.Error);
        // Without the custom definitions the same rules are rejected: unknown fields are never evaluated.
        Assert.Contains(MethodologyValidator.Validate(rules), i => i.Severity == IssueSeverity.Error && i.Message.Contains(f.Key));

        var r = MethodologyEngine.Evaluate(sites, rules, catalog).Sites.ToDictionary(x => x.SiteId);
        Assert.Equal(10, r["S1"].Score);        // valid value meets the threshold
        Assert.Equal(-2, r["S2"].Score);        // valid value below the threshold → default
        Assert.Equal(-5, r["S3"].Score);        // Unknown → explicit missing-data policy, nothing imputed
        Assert.True(r["S3"].Missing[0]);
        Assert.False(r["S1"].Missing[0]);
    }
}
