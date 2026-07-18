using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Configurations;

public sealed class TenantLogoMissingNotificationConfiguration : IEntityTypeConfiguration<TenantLogoMissingNotification>
{
    public void Configure(EntityTypeBuilder<TenantLogoMissingNotification> builder)
    {
        builder.ToTable("TenantLogoMissingNotifications");
        builder.HasKey(n => n.TenantId);
        builder.Property(n => n.TenantId).ValueGeneratedNever();

        builder.Property(n => n.LastDetectedAtUtc).IsRequired();
    }
}
