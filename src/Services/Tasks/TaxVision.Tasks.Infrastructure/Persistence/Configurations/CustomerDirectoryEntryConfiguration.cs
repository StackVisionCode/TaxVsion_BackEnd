using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class CustomerDirectoryEntryConfiguration : IEntityTypeConfiguration<CustomerDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<CustomerDirectoryEntry> builder)
    {
        builder.ToTable("CustomerDirectoryEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerId).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(300);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(x => new { x.TenantId, x.CustomerId })
            .IsUnique()
            .HasDatabaseName("IX_CustomerDirectoryEntries_TenantId_CustomerId");
        builder
            .HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("IX_CustomerDirectoryEntries_TenantId_DisplayName");
    }
}
