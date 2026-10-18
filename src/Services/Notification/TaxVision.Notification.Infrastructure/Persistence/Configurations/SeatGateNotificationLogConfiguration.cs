using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class SeatGateNotificationLogConfiguration : IEntityTypeConfiguration<SeatGateNotificationLog>
{
    public void Configure(EntityTypeBuilder<SeatGateNotificationLog> builder)
    {
        builder.ToTable("SeatGateNotificationLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.TenantId).IsRequired();
        builder.Property(log => log.UserId).IsRequired();
        builder.Property(log => log.SourceEventId).IsRequired();
        builder.Property(log => log.NotifiedAtUtc).IsRequired();

        builder.HasIndex(log => new
        {
            log.TenantId,
            log.UserId,
            log.ResolvedAtUtc,
        });
        builder.HasIndex(log => new { log.TenantId, log.UserId }).IsUnique().HasFilter("[ResolvedAtUtc] IS NULL");
    }
}
