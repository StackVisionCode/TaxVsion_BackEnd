using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class RefundLineConfiguration : IEntityTypeConfiguration<RefundLine>
{
    public void Configure(EntityTypeBuilder<RefundLine> builder)
    {
        builder.ToTable("RefundLines");
        builder.HasKey(refund => refund.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SaaSPayment._refunds (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(refund => refund.Id).ValueGeneratedNever();

        builder.Property(refund => refund.SaaSPaymentId).IsRequired();
        builder.Property(refund => refund.TenantId).IsRequired();

        builder.OwnsOne(refund => refund.Amount, money =>
        {
            money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.Property(refund => refund.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(refund => refund.ExternalRefundReference).HasMaxLength(200);
        builder.Property(refund => refund.RequestedByUserId).IsRequired();
        builder.Property(refund => refund.RefundedAtUtc).IsRequired();

        builder.HasIndex(refund => refund.SaaSPaymentId).HasDatabaseName("IX_RefundLines_SaaSPaymentId");
    }
}
