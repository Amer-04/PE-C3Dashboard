using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence.Configurations;

public class CandidateSiteConfiguration : IEntityTypeConfiguration<CandidateSite>
{
    public void Configure(EntityTypeBuilder<CandidateSite> b)
    {
        b.ToTable("CandidateSites");
        b.HasKey(x => x.Id);

        b.HasIndex(x => x.SiteId).IsUnique();
        b.HasIndex(x => new { x.Segment, x.ComputedRank });
        // Matches the default ranked-list sort (Score DESC, MilesToExit ASC) so the top page is an
        // index seek with no wide sort — avoids the large memory grant (RESOURCE_SEMAPHORE waits)
        // that could stall /api/sites. Also serves the MinScore filter.
        b.HasIndex(x => new { x.Score, x.MilesToExit })
            .HasDatabaseName("IX_CandidateSites_Score_MilesToExit")
            .IsDescending(true, false);
        b.HasIndex(x => x.County);

        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.Property(x => x.PamsPin).HasMaxLength(64).IsRequired();
        b.Property(x => x.SiteAddress).HasMaxLength(300).IsRequired();
        b.Property(x => x.Municipality).HasMaxLength(100).IsRequired();
        b.Property(x => x.County).HasMaxLength(50).IsRequired();
        b.Property(x => x.Block).HasMaxLength(500);
        b.Property(x => x.Lot);                                   // merged sites can list hundreds of lots → nvarchar(max)
        b.Property(x => x.Qual).HasMaxLength(50);
        b.Property(x => x.MemberLots);                            // nvarchar(max)
        b.Property(x => x.PropClass).HasMaxLength(20).IsRequired();
        b.Property(x => x.Zoning).HasMaxLength(50);
        b.Property(x => x.BuildingDescription).HasMaxLength(200);
        b.Property(x => x.LandDescription).HasMaxLength(200);
        b.Property(x => x.LastDeedDate).HasMaxLength(20);
        b.Property(x => x.OwnerName).HasMaxLength(200);
        b.Property(x => x.OwnerMailAddress).HasMaxLength(200);
        b.Property(x => x.OwnerMailCity).HasMaxLength(100);
        b.Property(x => x.OwnerMailZip).HasMaxLength(20);
        b.Property(x => x.BusinessesOnSite).HasMaxLength(1000);
        b.Property(x => x.BusinessCategories).HasMaxLength(1000);
        b.Property(x => x.BusinessPhone).HasMaxLength(50);
        b.Property(x => x.BusinessWebsite);                       // long tracking URLs → nvarchar(max)
        b.Property(x => x.DemandSummary).HasMaxLength(1000);
        b.Property(x => x.NearestPort).HasMaxLength(100);
        b.Property(x => x.NearestHighway).HasMaxLength(100);
        b.Property(x => x.NearestExit).HasMaxLength(50);
        b.Property(x => x.ExitName).HasMaxLength(100);
        b.Property(x => x.NearestDcfc).HasMaxLength(300);
        b.Property(x => x.NearestPowerEdisonSite).HasMaxLength(100);
        b.Property(x => x.Utility).HasMaxLength(100);
        b.Property(x => x.Highlands).HasMaxLength(100);
        b.Property(x => x.FloodZone100Yr).HasMaxLength(50);

        b.OwnsOne(x => x.Breakdown, o =>
        {
            o.Ignore(p => p.TruckDemand);
            o.Ignore(p => p.Total);
        });
        b.Navigation(x => x.Breakdown).IsRequired();

        b.HasMany(x => x.NearbyFleets)
            .WithOne(f => f.CandidateSite)
            .HasForeignKey(f => f.CandidateSiteId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
