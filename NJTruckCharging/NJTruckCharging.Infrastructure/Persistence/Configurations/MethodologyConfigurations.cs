using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NJTruckCharging.Domain.Entities;

namespace NJTruckCharging.Infrastructure.Persistence.Configurations;

public class MethodologyVersionConfiguration : IEntityTypeConfiguration<MethodologyVersion>
{
    public void Configure(EntityTypeBuilder<MethodologyVersion> b)
    {
        b.ToTable("MethodologyVersions");
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RulesJson).IsRequired();
        b.Property(x => x.RulesHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.PreviewRulesHash).HasMaxLength(64);
        b.Property(x => x.ChangeSummary).HasMaxLength(4000);
        // Exactly one baseline version.
        b.HasIndex(x => x.IsBaseline).IsUnique().HasFilter("[IsBaseline] = 1");
    }
}

public class MethodologyStateConfiguration : IEntityTypeConfiguration<MethodologyState>
{
    public void Configure(EntityTypeBuilder<MethodologyState> b)
    {
        b.ToTable("MethodologyState", t => t.HasCheckConstraint("CK_MethodologyState_Singleton", "[Id] = 1"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<MethodologyVersion>().WithMany().HasForeignKey(x => x.ActiveVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MethodologyVersion>().WithMany().HasForeignKey(x => x.PreviousActiveVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class MethodologyActivationConfiguration : IEntityTypeConfiguration<MethodologyActivation>
{
    public void Configure(EntityTypeBuilder<MethodologyActivation> b)
    {
        b.ToTable("MethodologyActivations");
        b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => x.VersionId);
    }
}

public class SiteVersionScoreConfiguration : IEntityTypeConfiguration<SiteVersionScore>
{
    public void Configure(EntityTypeBuilder<SiteVersionScore> b)
    {
        b.ToTable("SiteVersionScores");
        // Soft reference to CandidateSite.SiteId (no FK): survives re-imports, like the other user/derived tables.
        b.HasKey(x => new { x.MethodologyVersionId, x.SiteId });
        b.Property(x => x.SiteId).HasMaxLength(64);
        b.HasIndex(x => new { x.MethodologyVersionId, x.Segment, x.Rank });
        // Bounded (40 criteria max ≈ 1 KB): an nvarchar(max) column made SQL Server ask for large memory grants on bulk
        // inserts, which on a memory-starved LocalDB waited indefinitely (RESOURCE_SEMAPHORE).
        b.Property(x => x.PointsJson).HasMaxLength(4000).IsRequired();
        b.OwnsOne(x => x.Breakdown, o =>
        {
            o.Ignore(p => p.TruckDemand);
            o.Ignore(p => p.Total);
        });
        b.Navigation(x => x.Breakdown).IsRequired();
        b.HasOne<MethodologyVersion>().WithMany().HasForeignKey(x => x.MethodologyVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CustomDataFieldConfiguration : IEntityTypeConfiguration<CustomDataField>
{
    public void Configure(EntityTypeBuilder<CustomDataField> b)
    {
        b.ToTable("CustomDataFields");
        b.Property(x => x.Key).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
        b.Property(x => x.Label).HasMaxLength(80).IsRequired();
        b.Property(x => x.Description).HasMaxLength(600);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.Categories).HasMaxLength(1000);
        b.Ignore(x => x.CategoryList);
    }
}

public class CustomFieldValueConfiguration : IEntityTypeConfiguration<CustomFieldValue>
{
    public void Configure(EntityTypeBuilder<CustomFieldValue> b)
    {
        b.ToTable("CustomFieldValues");
        // Soft reference to CandidateSite.SiteId (no FK): values survive workbook re-imports.
        b.Property(x => x.SiteId).HasMaxLength(64).IsRequired();
        b.Property(x => x.FieldKey).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.FieldKey, x.SiteId }).IsUnique();
        b.HasIndex(x => x.SiteId);
        b.Property(x => x.CategoryValue).HasMaxLength(40);
        b.Property(x => x.Evidence).HasMaxLength(2000);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ImportBatch).HasMaxLength(200);
        b.HasOne<CustomDataField>().WithMany().HasPrincipalKey(f => f.Key).HasForeignKey(x => x.FieldKey).OnDelete(DeleteBehavior.Cascade);
    }
}
