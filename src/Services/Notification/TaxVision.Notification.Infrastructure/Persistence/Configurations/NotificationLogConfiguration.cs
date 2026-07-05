using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.TenantId).IsRequired();
        builder.Property(log => log.Channel).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(log => log.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(log => log.Subject).HasMaxLength(200).IsRequired();
        builder.Property(log => log.TemplateKey).HasMaxLength(64).IsRequired();
        builder.Property(log => log.Status).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(log => log.Error).HasMaxLength(512);
        builder.Property(log => log.CorrelationId).HasMaxLength(128);
        builder.Property(log => log.CreatedAtUtc).IsRequired();

        builder.HasIndex(log => new { log.TenantId, log.CreatedAtUtc });
        builder.HasIndex(log => new { log.TenantId, log.Status });
        builder.HasIndex(log => log.RelatedEventId);
    }
}
