using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Payments;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Cobros que no se pudieron aplicar. El índice único (TenantId, TenantPaymentId) hace idempotente la reentrega.</summary>
public sealed class UnappliedPaymentConfiguration : IEntityTypeConfiguration<UnappliedPayment>
{
    public void Configure(EntityTypeBuilder<UnappliedPayment> b)
    {
        b.ToTable("UnappliedPayments");
        b.HasKey(p => p.Id);

        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.TenantPaymentId).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.TenantPaymentId }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.ResolvedAtUtc });

        b.Property(p => p.ExternalReferenceId).HasMaxLength(UnappliedPayment.ReferenceMaxLength).IsRequired();
        b.Property(p => p.AmountCents).IsRequired();
        b.Property(p => p.Currency).HasColumnType("char(3)").IsRequired();
        b.Property(p => p.Reason).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(p => p.Resolution).HasConversion<string>().HasMaxLength(32);
        b.Property(p => p.ResolutionNote).HasMaxLength(UnappliedPayment.NoteMaxLength);
        b.Property(p => p.RowVersion).IsRowVersion();
    }
}
