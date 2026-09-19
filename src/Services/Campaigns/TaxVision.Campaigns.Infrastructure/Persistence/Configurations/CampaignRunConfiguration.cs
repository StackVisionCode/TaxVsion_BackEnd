using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignRunConfiguration : IEntityTypeConfiguration<CampaignRun>
{
    public void Configure(EntityTypeBuilder<CampaignRun> builder)
    {
        builder.ToTable("CampaignRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.CampaignId).IsRequired();
        builder.Property(r => r.TriggeredByUserId).IsRequired();
        builder.Property(r => r.TriggerKind).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.RejectionReason).HasMaxLength(200);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.FinishedAtUtc);
        builder.Property(r => r.RecipientCount).IsRequired();
        builder.Property(r => r.CounterDispatched).IsRequired();
        builder.Property(r => r.CounterAccepted).IsRequired();
        builder.Property(r => r.CounterDelivered).IsRequired();
        builder.Property(r => r.CounterFailed).IsRequired();
        builder.Property(r => r.CounterSkipped).IsRequired();
        builder.Property(r => r.CounterUnknown).IsRequired();
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder.HasMany(r => r.Recipients).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder
            .Metadata.FindNavigation(nameof(CampaignRun.Recipients))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => new { r.TenantId, r.CampaignId }).HasDatabaseName("IX_CampaignRuns_TenantId_CampaignId");
        builder.HasIndex(r => new { r.TenantId, r.Status }).HasDatabaseName("IX_CampaignRuns_TenantId_Status");
    }
}
