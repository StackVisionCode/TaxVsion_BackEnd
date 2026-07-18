using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class OutboundEmailMessageConfiguration : IEntityTypeConfiguration<OutboundEmailMessage>
{
    public void Configure(EntityTypeBuilder<OutboundEmailMessage> builder)
    {
        builder.ToTable("OutboundEmailMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TenantId).IsRequired();
        builder.Property(m => m.Subject).HasMaxLength(300).IsRequired();
        builder.Property(m => m.HtmlBody).IsRequired();
        builder.Property(m => m.TextBody).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.ProviderType).HasMaxLength(32);
        builder.Property(m => m.AttachmentFileIdsJson).IsRequired();
        builder.Property(m => m.Error).HasMaxLength(1024);
        builder.Property(m => m.CorrelationId).HasMaxLength(128);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        builder.HasMany(m => m.Recipients).WithOne().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(m => m.DeliveryLogs).WithOne().HasForeignKey(l => l.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(m => m.DeliveryLogs).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(m => new { m.TenantId, m.CreatedAtUtc });
        builder.HasIndex(m => new { m.TenantId, m.Status });
        builder.HasIndex(m => m.CampaignId);
    }
}

public sealed class EmailRecipientConfiguration : IEntityTypeConfiguration<EmailRecipient>
{
    public void Configure(EntityTypeBuilder<EmailRecipient> builder)
    {
        builder.ToTable("EmailRecipients");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.MessageId).IsRequired();
        builder.Property(r => r.Address).HasMaxLength(320).IsRequired();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(8).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.HasIndex(r => r.MessageId);
    }
}

public sealed class EmailDeliveryLogConfiguration : IEntityTypeConfiguration<EmailDeliveryLog>
{
    public void Configure(EntityTypeBuilder<EmailDeliveryLog> builder)
    {
        builder.ToTable("EmailDeliveryLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.MessageId).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(l => l.Detail).HasMaxLength(1024);
        builder.Property(l => l.AttemptedAtUtc).IsRequired();
        builder.HasIndex(l => l.MessageId);
    }
}
