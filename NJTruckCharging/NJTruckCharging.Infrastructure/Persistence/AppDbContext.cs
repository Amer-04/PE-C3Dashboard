using Microsoft.EntityFrameworkCore;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CandidateSite> CandidateSites => Set<CandidateSite>();
    public DbSet<NearbyFleet> NearbyFleets => Set<NearbyFleet>();
    public DbSet<SegmentInfo> Segments => Set<SegmentInfo>();
    public DbSet<PowerEdisonSite> PowerEdisonSites => Set<PowerEdisonSite>();
    public DbSet<Interchange> Interchanges => Set<Interchange>();
    public DbSet<MethodologyNote> MethodologyNotes => Set<MethodologyNote>();
    public DbSet<SavedSite> SavedSites => Set<SavedSite>();
    public DbSet<SiteContact> SiteContacts => Set<SiteContact>();
    public DbSet<FeasibilityItem> FeasibilityItems => Set<FeasibilityItem>();
    public DbSet<SiteScreening> SiteScreenings => Set<SiteScreening>();
    public DbSet<SiteScreeningEntry> SiteScreeningEntries => Set<SiteScreeningEntry>();
    public DbSet<ResearchHistoryEntry> ResearchHistory => Set<ResearchHistoryEntry>();
    public DbSet<MethodologyVersion> MethodologyVersions => Set<MethodologyVersion>();
    public DbSet<MethodologyState> MethodologyState => Set<MethodologyState>();
    public DbSet<MethodologyActivation> MethodologyActivations => Set<MethodologyActivation>();
    public DbSet<SiteVersionScore> SiteVersionScores => Set<SiteVersionScore>();
    public DbSet<CustomDataField> CustomDataFields => Set<CustomDataField>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        if (Database.IsPostgres()) ApplyPostgresAdjustments(modelBuilder);
    }

    /// <summary>
    /// The configurations are written for SQL Server. On PostgreSQL (the Supabase cloud deployment) the tables live in
    /// their own schema — outside Supabase's API-exposed "public" schema — and the few SQL Server expressions are
    /// rewritten. Column names, types and keys are otherwise identical, so data copies 1:1.
    /// </summary>
    private static void ApplyPostgresAdjustments(ModelBuilder b)
    {
        b.HasDefaultSchema(DatabaseProvider.PostgresSchema);
        b.Entity<MethodologyVersion>().HasIndex(x => x.IsBaseline).IsUnique().HasFilter("\"IsBaseline\" = TRUE");
        b.Entity<MethodologyState>().ToTable("MethodologyState",
            t => t.HasCheckConstraint("CK_MethodologyState_Singleton", "\"Id\" = 1"));
        // PostgreSQL has no auto-updating rowversion column: keep the stored value as a plain concurrency token.
        b.Entity<MethodologyState>().Property(x => x.RowVersion).ValueGeneratedNever().IsConcurrencyToken();
    }
}
