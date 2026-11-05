using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Infrastructure.Persistence.Configurations;

public sealed class CustomerContactPolicyProjectionConfiguration
    : IEntityTypeConfiguration<CustomerContactPolicyProjection>
{
    public void Configure(EntityTypeBuilder<CustomerContactPolicyProjection> builder)
    {
        builder.ToTable("CustomerContactPolicyProjections");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.CustomerId).IsRequired();
        // Sin default en la base: con HasDefaultValue(true) EF omite el false al insertar y gana el true.
        builder.Property(e => e.SmsEnabled).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();

        // Un cliente, una fila: la carrera entre evento y reconciliación choca en el índice.
        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).IsUnique();
    }
}
