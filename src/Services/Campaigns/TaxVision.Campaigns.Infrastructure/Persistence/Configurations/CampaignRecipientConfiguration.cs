using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Configurations;

public sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> builder)
    {
        builder.ToTable("CampaignRecipients");
        builder.HasKey(r => r.Id);
        // Id generado en dominio (guardrail 10): sin esto EF haría UPDATE en vez de INSERT al agregar hijos.
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.RunId).IsRequired();
        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.ContactRef).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Channel).HasConversion<int>().IsRequired();
        builder.Property(r => r.Email).HasMaxLength(320);
        builder.Property(r => r.PhoneE164).HasMaxLength(20);
        builder.Property(r => r.DispatchId).HasMaxLength(80).IsRequired();
        builder.Property(r => r.AttemptNo).IsRequired();
        builder.Property(r => r.State).HasConversion<int>().IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(200);
        builder.Property(r => r.ProviderRef).HasMaxLength(200);
        builder.Property(r => r.AcceptedAtUtc);
        builder.Property(r => r.DeliveredAtUtc);
        builder.Property(r => r.SettledAtUtc);

        builder.HasIndex(r => new { r.RunId, r.DispatchId }).IsUnique().HasDatabaseName("IX_CampaignRecipients_RunId_DispatchId");
        builder
            .HasIndex(r => new { r.RunId, r.ContactRef, r.Channel })
            .IsUnique()
            .HasDatabaseName("IX_CampaignRecipients_RunId_ContactRef_Channel");
        builder.HasIndex(r => new { r.RunId, r.State }).HasDatabaseName("IX_CampaignRecipients_RunId_State");
    }
}
