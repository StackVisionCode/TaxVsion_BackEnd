using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class TenantOwnerProjectionConfiguration : IEntityTypeConfiguration<TenantOwnerProjection>
{
    public void Configure(EntityTypeBuilder<TenantOwnerProjection> builder)
    {
        builder.ToTable("TenantOwnerProjections");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.RegisteredAtUtc).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.UserId }).IsUnique();
        builder.HasIndex(p => new
        {
            p.TenantId,
            p.IsActive,
            p.RegisteredAtUtc,
        });
    }
}
