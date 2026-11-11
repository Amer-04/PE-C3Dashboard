using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NJTruckCharging.Application.Methodology;
using NJTruckCharging.Controllers;
using NJTruckCharging.Security;

namespace NJTruckCharging.Tests.Unit;

/// <summary>Start-up configuration, the read-only exception and the recalculation gate of shared methodology editing.</summary>
public class MethodologyEditingTests
{
    private const string ReadOnlyLogin = "Host=h;Username=c3_runtime_readonly.ref;Password=x;Database=postgres";
    private const string WriterLogin = "Host=h;Username=c3_methodology_writer.ref;Password=y;Database=postgres";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static (string, string?)[] Cloud(params (string, string?)[] extra) =>
    [
        ("Database:Provider", "Postgres"), ("Auth:Mode", "Supabase"), ("ConnectionStrings:DefaultConnection", ReadOnlyLogin), .. extra
    ];

    [Fact]
    public void Editing_off_needs_nothing_and_changes_nothing()
    {
        MethodologyEditing.ValidateConfiguration(Config(Cloud()));
        Assert.False(MethodologyEditing.IsCloudEditingEnabled(Config(Cloud())));
        Assert.False(MethodologyEditing.IsCloudEditingEnabled(Config(Cloud(("Methodology:CloudEditing:Enabled", "")))));
    }

    [Fact]
    public void Cloud_editing_with_a_separate_writer_login_starts()
    {
        var c = Config(Cloud(("Methodology:CloudEditing:Enabled", "true"), ("ConnectionStrings:MethodologyWriter", WriterLogin)));
        MethodologyEditing.ValidateConfiguration(c);
        Assert.True(MethodologyEditing.IsCloudEditingEnabled(c));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cloud_editing_without_a_writer_login_refuses_to_start(string? writer)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MethodologyEditing.ValidateConfiguration(
            Config(Cloud(("Methodology:CloudEditing:Enabled", "true"), ("ConnectionStrings:MethodologyWriter", writer)))));
        Assert.Contains("MethodologyWriter", ex.Message);
    }

    [Theory]
    [InlineData(ReadOnlyLogin)]
    [InlineData("Host=h;Username=C3_RUNTIME_READONLY.ref;Password=other;Database=postgres")]
    [InlineData("Host=h;Password=nouser;Database=postgres")]
    public void Cloud_editing_never_reuses_the_read_only_login_for_writes(string writer)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MethodologyEditing.ValidateConfiguration(
            Config(Cloud(("Methodology:CloudEditing:Enabled", "true"), ("ConnectionStrings:MethodologyWriter", writer)))));
        Assert.Contains("own login", ex.Message);
    }

    [Fact]
    public void Cloud_editing_requires_supabase_sign_in()
    {
        var c = Config(("Database:Provider", "Postgres"), ("Auth:Mode", "None"), ("ConnectionStrings:DefaultConnection", ReadOnlyLogin),
            ("Methodology:CloudEditing:Enabled", "true"), ("ConnectionStrings:MethodologyWriter", WriterLogin));
        Assert.Throws<InvalidOperationException>(() => MethodologyEditing.ValidateConfiguration(c));
    }

    // ---- the read-only exception ---------------------------------------------------------------------------------

    private static HttpContext Request(object? metadata)
    {
        var ctx = new DefaultHttpContext();
        if (metadata is not null)
            ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        return ctx;
    }

    [Fact]
    public void Only_marked_editor_endpoints_pass_read_only_mode_and_only_when_editing_is_on()
    {
        Assert.True(MethodologyEditing.AllowedInReadOnlyMode(Request(new RequireMethodologyEditorAttribute()), true));
        Assert.False(MethodologyEditing.AllowedInReadOnlyMode(Request(new RequireMethodologyEditorAttribute()), false));
        Assert.False(MethodologyEditing.AllowedInReadOnlyMode(Request(new RequireMethodologyAdminAttribute()), true));   // custom fields
        Assert.False(MethodologyEditing.AllowedInReadOnlyMode(Request(new object()), true));
        Assert.False(MethodologyEditing.AllowedInReadOnlyMode(Request(null), true));   // unrouted request
    }

    /// <summary>
    /// The exception is driven by endpoint metadata, so it must sit on exactly the methodology-changing actions —
    /// never on any other controller (saved sites, contacts, research, screening, feasibility, custom fields, admin).
    /// </summary>
    [Fact]
    public void Exactly_the_methodology_change_endpoints_carry_the_editor_marker()
    {
        var marked = typeof(MethodologyController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<RequireMethodologyEditorAttribute>() is not null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .OrderBy(x => x).ToList();
        Assert.Equal(new[]
        {
            "MethodologyController.CreateDraft", "MethodologyController.DeleteDraft", "MethodologyController.Duplicate",
            "MethodologyController.Preview", "MethodologyController.Publish", "MethodologyController.UpdateDraft",
            "MethodologyController.Validate",
        }, marked);

        // And every non-GET methodology action is marked (none can slip past the editor check).
        var unmarked = typeof(MethodologyController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute h && !h.HttpMethods.Contains("GET")))
            .Where(m => m.GetCustomAttribute<RequireMethodologyEditorAttribute>() is null)
            .Select(m => m.Name).ToList();
        Assert.Empty(unmarked);
    }

    // ---- one recalculation at a time ------------------------------------------------------------------------------

    [Fact]
    public void The_scoring_gate_admits_one_job_at_a_time_and_reopens_when_it_ends()
    {
        var gate = new ScoringJobGate();
        var first = gate.TryEnter();
        Assert.NotNull(first);
        Assert.Null(gate.TryEnter());
        first!.Dispose();
        first.Dispose();   // a second dispose must not release a slot it does not hold
        var second = gate.TryEnter();
        Assert.NotNull(second);
        Assert.Null(gate.TryEnter());
        second!.Dispose();
    }

    [Fact]
    public void The_actor_for_system_operations_has_no_identity()
    {
        Assert.Null(MethodologyActor.System.UserId);
        Assert.Null(MethodologyActor.System.Email);
    }
}
