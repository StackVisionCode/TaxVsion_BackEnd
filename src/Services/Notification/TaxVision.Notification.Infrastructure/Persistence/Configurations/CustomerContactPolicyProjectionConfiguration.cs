using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Infrastructure.Persistence.Configurations;

public sealed class CustomerContactPolicyProjectionConfiguration
    : IEntityTypeConfiguration<CustomerContactPolicyProjection>
{
    public void Configure(EntityTypeBuilder<CustomerContactPolicyProjection> builder)
    {
        builder.ToTable("CustomerContactPolicyProjections");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.CustomerId).IsRequired();
        builder.Property(e => e.EmailEnabled).HasDefaultValue(true).IsRequired();
        builder.Property(e => e.SmsEnabled).HasDefaultValue(true).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        // Un cliente, una fila. El unique convierte en choque visible la carrera entre created/updated.
        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).IsUnique();
    }
}
