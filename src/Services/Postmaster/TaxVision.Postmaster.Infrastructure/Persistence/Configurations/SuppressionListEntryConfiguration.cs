using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Configurations;

public sealed class SuppressionListEntryConfiguration : IEntityTypeConfiguration<SuppressionListEntry>
{
    public void Configure(EntityTypeBuilder<SuppressionListEntry> builder)
    {
        builder.ToTable("SuppressionListEntries");
        builder.HasKey(e => new { e.TenantId, e.EmailAddress });
        builder.Property(e => e.EmailAddress).HasMaxLength(320);
        builder.Property(e => e.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.AddedAtUtc).IsRequired();
        builder.Property(e => e.AddedByUserId);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.HasIndex(e => new
        {
            e.TenantId,
            e.Reason,
            e.AddedAtUtc,
        });
    }
}
