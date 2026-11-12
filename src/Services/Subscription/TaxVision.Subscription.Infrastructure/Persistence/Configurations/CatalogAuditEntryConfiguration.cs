using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class CatalogAuditEntryConfiguration : IEntityTypeConfiguration<CatalogAuditEntry>
{
    public void Configure(EntityTypeBuilder<CatalogAuditEntry> builder)
    {
        builder.ToTable("CatalogAuditEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.ActorKind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(entry => entry.ActorLabel).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(100).IsRequired();
        builder.Property(entry => entry.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.EntityCode).HasMaxLength(200);
        builder.Property(entry => entry.Reason).HasMaxLength(CatalogAuditEntry.MaxReasonLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();
        builder.HasIndex(entry => entry.OccurredAtUtc);
        builder.HasIndex(entry => new
        {
            entry.EntityType,
            entry.EntityId,
            entry.OccurredAtUtc,
        });
    }
}
