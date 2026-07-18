using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration para <see cref="NotificationDispatchAttempt"/>. FK a <see cref="NotificationLog"/>
/// con CASCADE (borrar el log borra sus attempts). Nota: EF descubre la colección
/// <c>NotificationLog.Attempts</c> automáticamente via convention (mismo tipo hijo + FK), no requiere
/// mapping explícito adicional en el padre.
/// </summary>
public sealed class NotificationDispatchAttemptConfiguration : IEntityTypeConfiguration<NotificationDispatchAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDispatchAttempt> builder)
    {
        builder.ToTable("NotificationDispatchAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.NotificationLogId).IsRequired();
        builder.Property(a => a.Channel).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(25).IsRequired();
        builder.Property(a => a.ProviderMessageId).HasMaxLength(200);
        builder.Property(a => a.QueuedAtUtc).IsRequired();
        builder.Property(a => a.LastEventAtUtc);
        builder.Property(a => a.ErrorReason).HasMaxLength(500);
        builder.Property(a => a.Metadata).HasColumnType("nvarchar(max)");

        builder
            .HasOne<NotificationLog>()
            .WithMany(l => l.Attempts)
            .HasForeignKey(a => a.NotificationLogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.NotificationLogId);
        builder.HasIndex(a => new
        {
            a.TenantId,
            a.Channel,
            a.Status,
            a.QueuedAtUtc,
        });
    }
}
