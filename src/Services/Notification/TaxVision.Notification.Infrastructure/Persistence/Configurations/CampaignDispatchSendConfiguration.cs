using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class CampaignDispatchSendConfiguration : IEntityTypeConfiguration<CampaignDispatchSend>
{
    public void Configure(EntityTypeBuilder<CampaignDispatchSend> builder)
    {
        builder.ToTable("CampaignDispatchSends");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DispatchId).HasMaxLength(CampaignDispatchSend.DispatchIdMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(CampaignDispatchSend.ProviderMessageIdMaxLength);
        builder.Property(x => x.FailureReason).HasMaxLength(CampaignDispatchSend.FailureReasonMaxLength);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        // Quien inserta primero envía: el perdedor de dos entregas simultáneas choca aquí.
        builder
            .HasIndex(x => new { x.TenantId, x.DispatchId })
            .IsUnique()
            .HasDatabaseName("IX_CampaignDispatchSends_TenantId_DispatchId_Unique");
    }
}
