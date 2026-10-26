using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

internal sealed class ClientFormNotificationSettingsProjectionConfiguration
    : IEntityTypeConfiguration<ClientFormNotificationSettingsProjection>
{
    public void Configure(EntityTypeBuilder<ClientFormNotificationSettingsProjection> builder)
    {
        builder.ToTable("ClientFormNotificationSettingsProjections");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.TenantId).IsRequired();
        builder.Property(row => row.PushNotificationsEnabled).HasDefaultValue(false).IsRequired();
        builder.HasIndex(row => row.TenantId).IsUnique();
    }
}
