using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class CustomerPortalAccountProjectionConfiguration
    : IEntityTypeConfiguration<CustomerPortalAccountProjection>
{
    public void Configure(EntityTypeBuilder<CustomerPortalAccountProjection> builder)
    {
        builder.ToTable("CustomerPortalAccountProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.CustomerId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.CustomerId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.IsActive });
    }
}
