using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.CreatedByUserId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(Campaign.MaxNameLength).IsRequired();
        builder.Property(c => c.Subject).HasMaxLength(Campaign.MaxSubjectLength);
        builder.Property(c => c.Message).HasMaxLength(Campaign.MaxMessageLength).IsRequired();
        builder.Property(c => c.Channels).HasConversion<int>().IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Status }).HasDatabaseName("IX_Campaigns_TenantId_Status");
        builder
            .HasIndex(c => new { c.TenantId, c.CreatedAtUtc })
            .HasDatabaseName("IX_Campaigns_TenantId_CreatedAtUtc");
    }
}
