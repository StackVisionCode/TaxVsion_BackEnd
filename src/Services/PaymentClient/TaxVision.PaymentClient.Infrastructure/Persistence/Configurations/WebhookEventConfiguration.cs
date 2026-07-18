using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Webhooks;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("WebhookEvents");
        builder.HasKey(webhookEvent => webhookEvent.Id);

        builder.Property(webhookEvent => webhookEvent.TenantId).IsRequired();
        builder
            .Property(webhookEvent => webhookEvent.ProviderCode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(webhookEvent => webhookEvent.ProviderEventId).HasMaxLength(200).IsRequired();
        builder.Property(webhookEvent => webhookEvent.EventType).HasMaxLength(100).IsRequired();
        builder.Property(webhookEvent => webhookEvent.ReceivedAtUtc).IsRequired();
        builder.Property(webhookEvent => webhookEvent.RawPayload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(webhookEvent => webhookEvent.SignatureHeader).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(webhookEvent => webhookEvent.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(webhookEvent => webhookEvent.ProcessingError).HasMaxLength(2000);

        builder
            .HasIndex(webhookEvent => new
            {
                webhookEvent.TenantId,
                webhookEvent.ProviderCode,
                webhookEvent.ProviderEventId,
            })
            .IsUnique()
            .HasDatabaseName("UX_WebhookEvents_TenantId_ProviderCode_ProviderEventId");
    }
}
