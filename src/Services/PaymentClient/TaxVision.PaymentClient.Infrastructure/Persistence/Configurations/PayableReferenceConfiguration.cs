using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Payables;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class PayableReferenceConfiguration : IEntityTypeConfiguration<PayableReference>
{
    public void Configure(EntityTypeBuilder<PayableReference> builder)
    {
        builder.ToTable("PayableReferences");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PurposeKind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.ExternalReferenceId).HasMaxLength(200).IsRequired();

        builder.OwnsOne(
            p => p.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(p => p.Reference).HasMaxLength(64).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        // La referencia opaca es la clave de lookup del resolver público — única.
        builder.HasIndex(p => p.Reference).IsUnique().HasDatabaseName("UX_PayableReferences_Reference");

        // Idempotencia del ensure: un payable por (tenant, propósito, recurso externo).
        builder
            .HasIndex(p => new { p.TenantId, p.PurposeKind, p.ExternalReferenceId })
            .IsUnique()
            .HasDatabaseName("UX_PayableReferences_Tenant_Purpose_ExternalRef");
    }
}
