using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class AppliedCatalogRevisionConfiguration : IEntityTypeConfiguration<AppliedCatalogRevision>
{
    public void Configure(EntityTypeBuilder<AppliedCatalogRevision> builder)
    {
        builder.ToTable("CatalogRevisions");
        builder.HasKey(revision => revision.RevisionId);
        builder.Property(revision => revision.RevisionId).HasMaxLength(100);
        builder
            .Property(revision => revision.Summary)
            .HasMaxLength(AppliedCatalogRevision.MaxSummaryLength)
            .IsRequired();
        builder.Property(revision => revision.AppliedAtUtc).IsRequired();
        builder.Property(revision => revision.FollowUpPublishedAtUtc);
    }
}
