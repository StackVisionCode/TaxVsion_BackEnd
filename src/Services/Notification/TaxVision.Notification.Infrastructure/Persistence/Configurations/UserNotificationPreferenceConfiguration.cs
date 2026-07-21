using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> builder)
    {
        builder.ToTable("UserNotificationPreferences");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(p => p.Channel).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(p => p.Enabled).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        // Opt-out: solo existe una fila cuando el usuario cambió el default (Enabled=true).
        builder
            .HasIndex(p => new
            {
                p.TenantId,
                p.UserId,
                p.Category,
                p.Channel,
            })
            .IsUnique();
    }
}
