using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NJTruckCharging.Infrastructure.Persistence;

namespace NJTruckCharging.Tests.Integration;

/// <summary>
/// The EF migration AddMethodologyEditingAudit on a POPULATED database: up keeps every existing row, down (rollback)
/// keeps every existing row and removes only the audit schema, and up again works. Uses its own throw-away LocalDB.
/// </summary>
public class MethodologyMigrationRoundTripTests
{
    private const string Previous = "20261109112128_AddResearchEvidenceHistory";
    private const string Migration = "20261111163941_AddMethodologyEditingAudit";
    private const string Connection =
        @"Server=(localdb)\MSSQLLocalDB;Database=NJTruckCharging_MigrationRoundTripTest;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    /// <summary>Existing data, read with the columns that existed BEFORE the migration (so up/down can be compared).</summary>
    private const string SnapshotSql = """
        SELECT
          (SELECT [Id],[Name],[Status],[IsBaseline],[RulesJson],[RulesHash],[ChangeSummary],[BasedOnVersionId],[CreatedAtUtc],[UpdatedAtUtc],
                  [PublishedAtUtc],[PreviewRulesHash],[ScoresDataVersion],[PreviewedAtUtc],[PreviewSummaryJson]
           FROM [MethodologyVersions] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)
        + '|' + (SELECT [Id],[VersionId],[PreviousVersionId],[Reason],[AtUtc],[SitesScored],[DataVersion],[Note]
           FROM [MethodologyActivations] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)
        + '|' + (SELECT * FROM [MethodologyState] FOR JSON PATH, INCLUDE_NULL_VALUES)
        + '|' + (SELECT * FROM [SavedSites] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)
        + '|' + (SELECT * FROM [SiteScreenings] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)
        + '|' + (SELECT * FROM [ResearchHistoryEntries] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES)
        """;

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(Connection).Options);

    private static async Task<string> SnapshotAsync(AppDbContext db) =>
        (await db.Database.SqlQueryRaw<string>(SnapshotSql + " AS [Value]").ToListAsync()).Single();

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column) =>
        (await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'").ToListAsync()).Single() == 1;

    [Fact]
    public async Task Migration_up_and_rollback_keep_every_existing_row()
    {
        await using var db = Db();
        await db.Database.EnsureDeletedAsync();
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO [MethodologyVersions] ([Name],[Status],[IsBaseline],[RulesJson],[RulesHash],[CreatedAtUtc],[UpdatedAtUtc],[PublishedAtUtc])
                  VALUES ('Original', 'Published', 1, '{{"schemaVersion":1,"criteria":[]}}', 'h1', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()),
                         ('Team draft', 'Draft', 0, '{{"schemaVersion":1,"criteria":[]}}', 'h2', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NULL);
                INSERT INTO [MethodologyState] ([Id],[ActiveVersionId],[DataVersion]) VALUES (1, 1, 3);
                INSERT INTO [MethodologyActivations] ([VersionId],[Reason],[AtUtc],[SitesScored],[Note]) VALUES (1, 'Initial', SYSDATETIMEOFFSET(), 30584, 'baseline');
                INSERT INTO [SavedSites] ([SiteId],[Notes],[CreatedAtUtc],[UpdatedAtUtc]) VALUES ('1201_2706_2', 'keep me', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                INSERT INTO [SiteScreenings] ([SiteId],[Decision],[Evidence],[CreatedAtUtc],[UpdatedAtUtc]) VALUES ('1201_2706_2', 'Promising', 'site visit', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                INSERT INTO [ResearchHistoryEntries] ([SiteId],[Subject],[Action],[Note],[RecordedAtUtc]) VALUES ('1201_2706_2', 'Site', 'Started', 'keep', SYSDATETIMEOFFSET());
                """);
            var before = await SnapshotAsync(db);

            // Up: additive. Existing rows unchanged; new columns start empty (revision 0, no editor recorded).
            await migrator.MigrateAsync(Migration);
            Assert.Equal(before, await SnapshotAsync(db));
            Assert.True(await ColumnExistsAsync(db, "MethodologyVersions", "Revision"));
            Assert.Equal(0, (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [MethodologyVersions] WHERE [Revision] <> 0 OR [CreatedByEmail] IS NOT NULL").ToListAsync()).Single());
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE [MethodologyVersions] SET [Revision] = 4, [UpdatedByEmail] = 'rules.editor@poweredison.com' WHERE [Id] = 2;
                INSERT INTO [MethodologyChangeLog] ([VersionId],[VersionName],[Action],[AtUtc],[ActorEmail]) VALUES (2, 'Team draft', 'SaveDraft', SYSDATETIMEOFFSET(), 'rules.editor@poweredison.com');
                """);

            // Down (rollback): only the audit schema goes; every pre-existing row is identical.
            await migrator.MigrateAsync(Previous);
            Assert.Equal(before, await SnapshotAsync(db));
            Assert.False(await ColumnExistsAsync(db, "MethodologyVersions", "Revision"));
            Assert.False(await ColumnExistsAsync(db, "MethodologyActivations", "ActorEmail"));
            Assert.Equal(0, (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'MethodologyChangeLog'").ToListAsync()).Single());

            // Up again (re-deploy after a rollback) works and still preserves everything.
            await migrator.MigrateAsync(Migration);
            Assert.Equal(before, await SnapshotAsync(db));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
