using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence.Configurations;

public class NearbyFleetConfiguration : IEntityTypeConfiguration<NearbyFleet>
{
    public void Configure(EntityTypeBuilder<NearbyFleet> b)
    {
        b.ToTable("NearbyFleets");
        b.Property(x => x.Carrier).HasMaxLength(200).IsRequired();
        b.Property(x => x.Dba).HasMaxLength(200);
        b.Property(x => x.CarrierAddress).HasMaxLength(300);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.UsDot).HasMaxLength(20);
        b.Property(x => x.Operation).HasMaxLength(50);
        b.Property(x => x.Cargo).HasMaxLength(500);
        b.HasIndex(x => x.CandidateSiteId);
    }
}

public class SegmentInfoConfiguration : IEntityTypeConfiguration<SegmentInfo>
{
    public void Configure(EntityTypeBuilder<SegmentInfo> b)
    {
        b.ToTable("Segments");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Counties).HasMaxLength(200).IsRequired();
        b.Property(x => x.No1Site).HasMaxLength(300);
        b.Property(x => x.No1Municipality).HasMaxLength(100);
        b.Property(x => x.No1Businesses).HasMaxLength(500);
        b.Property(x => x.No1Highway).HasMaxLength(100);
        b.Property(x => x.PowerEdisonSitesInSegment).HasMaxLength(300);
    }
}

public class PowerEdisonSiteConfiguration : IEntityTypeConfiguration<PowerEdisonSite>
{
    public void Configure(EntityTypeBuilder<PowerEdisonSite> b)
    {
        b.ToTable("PowerEdisonSites");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.Property(x => x.ShortName).HasMaxLength(50).IsRequired();
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Municipality).HasMaxLength(100).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
    }
}

public class InterchangeConfiguration : IEntityTypeConfiguration<Interchange>
{
    public void Configure(EntityTypeBuilder<Interchange> b)
    {
        b.ToTable("Interchanges");
        b.Property(x => x.Highway).HasMaxLength(100).IsRequired();
        b.Property(x => x.Exit).HasMaxLength(50);
        b.Property(x => x.Destination).HasMaxLength(300);
    }
}

public class MethodologyNoteConfiguration : IEntityTypeConfiguration<MethodologyNote>
{
    public void Configure(EntityTypeBuilder<MethodologyNote> b)
    {
        b.ToTable("MethodologyNotes");
        b.Property(x => x.Topic).HasMaxLength(200).IsRequired();
        b.Property(x => x.Detail).IsRequired();
    }
}

public class SavedSiteConfiguration : IEntityTypeConfiguration<SavedSite>
{
    public void Configure(EntityTypeBuilder<SavedSite> b)
    {
        b.ToTable("SavedSites");
        // Soft reference to CandidateSite.SiteId (no FK): bookmarks must survive workbook re-imports,
        // which delete and re-insert every CandidateSite row.
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.SiteId).IsUnique();
        b.Property(x => x.Notes).HasMaxLength(4000);
    }
}

public class SiteContactConfiguration : IEntityTypeConfiguration<SiteContact>
{
    public void Configure(EntityTypeBuilder<SiteContact> b)
    {
        b.ToTable("SiteContacts");
        // Soft reference to CandidateSite.SiteId (no FK); many contacts per site.
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.SiteId);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Organization).HasMaxLength(200);
        b.Property(x => x.JobTitle).HasMaxLength(200);
        b.Property(x => x.ContactType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.BusinessPhone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.SourceUrl).HasMaxLength(1000);
        b.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.VerificationStatus);
        b.Property(x => x.VerifiedBy).HasMaxLength(200);
        b.Property(x => x.ResearchNotes).HasMaxLength(4000);
        b.Property(x => x.CreatedBy).HasMaxLength(200);
    }
}

public class FeasibilityItemConfiguration : IEntityTypeConfiguration<FeasibilityItem>
{
    public void Configure(EntityTypeBuilder<FeasibilityItem> b)
    {
        b.ToTable("FeasibilityItems");
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.Property(x => x.ItemKey).HasConversion<string>().HasMaxLength(40);
        // One row per check per site.
        b.HasIndex(x => new { x.SiteId, x.ItemKey }).IsUnique();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Evidence).HasMaxLength(4000);
        b.Property(x => x.Reviewer).HasMaxLength(200);
        b.Property(x => x.SourceUrl).HasMaxLength(2048);
        b.Property(x => x.EvidenceDate).HasColumnType("date");
    }
}

public class ResearchHistoryEntryConfiguration : IEntityTypeConfiguration<ResearchHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ResearchHistoryEntry> b)
    {
        b.ToTable("ResearchHistoryEntries");
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.SiteId, x.RecordedAtUtc });
        b.Property(x => x.Subject).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ItemKey).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.PreviousStatus).HasMaxLength(20);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.Evidence).HasMaxLength(4000);
        b.Property(x => x.SourceUrl).HasMaxLength(2048);
        b.Property(x => x.EvidenceDate).HasColumnType("date");
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.RecordedBy).HasMaxLength(200);
    }
}

public class SiteScreeningConfiguration : IEntityTypeConfiguration<SiteScreening>
{
    public void Configure(EntityTypeBuilder<SiteScreening> b)
    {
        b.ToTable("SiteScreenings");
        // Soft reference by stable SiteId (no FK) so decisions survive workbook re-imports.
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.SiteId).IsUnique();
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(24);
        b.HasIndex(x => x.Decision);
        b.Property(x => x.Evidence).HasMaxLength(4000);
    }
}

public class SiteScreeningEntryConfiguration : IEntityTypeConfiguration<SiteScreeningEntry>
{
    public void Configure(EntityTypeBuilder<SiteScreeningEntry> b)
    {
        b.ToTable("SiteScreeningEntries");
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.SiteId, x.RecordedAtUtc });
        b.Property(x => x.Decision).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Evidence).HasMaxLength(4000);
    }
}
