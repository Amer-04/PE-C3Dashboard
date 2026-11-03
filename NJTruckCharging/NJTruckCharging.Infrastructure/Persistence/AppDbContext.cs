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

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
