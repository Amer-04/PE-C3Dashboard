using Microsoft.Extensions.Configuration;
using NJTruckCharging.Infrastructure;

namespace NJTruckCharging.Tests.Unit;

public class MigrationGuardTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static readonly string[] Pending = ["20261109000000_AddResearchHistory"];

    [Fact]
    public void Refuses_pending_migrations_on_the_live_database_by_default()
    {
        var reason = MigrationGuard.Check("NJTruckCharging", Pending, Config());
        Assert.NotNull(reason);
        Assert.Contains("NJTruckCharging", reason);
        Assert.Contains("AddResearchHistory", reason);
    }

    [Fact]
    public void Protected_name_match_is_case_insensitive()
        => Assert.NotNull(MigrationGuard.Check("njtruckcharging", Pending, Config()));

    [Theory]
    [InlineData("NJTruckCharging_Demo")]
    [InlineData("NJTruckCharging_IntegrationTest")]
    public void Allows_demo_and_test_databases(string db)
        => Assert.Null(MigrationGuard.Check(db, Pending, Config()));

    [Fact]
    public void Allows_the_live_database_when_nothing_is_pending()
        => Assert.Null(MigrationGuard.Check("NJTruckCharging", [], Config()));

    [Fact]
    public void Explicit_approval_flag_allows_the_protected_database()
        => Assert.Null(MigrationGuard.Check("NJTruckCharging", Pending, Config(("Database:AllowProtectedMigration", "true"))));

    [Fact]
    public void Configured_protected_names_replace_the_default()
    {
        var config = Config(("Database:ProtectedNames:0", "C3_Production"));
        Assert.NotNull(MigrationGuard.Check("C3_Production", Pending, config));
        Assert.Null(MigrationGuard.Check("NJTruckCharging", Pending, config));
    }
}
