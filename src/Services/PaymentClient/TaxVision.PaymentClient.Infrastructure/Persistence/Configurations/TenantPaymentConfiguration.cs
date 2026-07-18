using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantPaymentConfiguration : IEntityTypeConfiguration<TenantPayment>
{
    public void Configure(EntityTypeBuilder<TenantPayment> builder)
    {
        builder.ToTable("TenantPayments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.TenantId).IsRequired();

        builder
            .Property(payment => payment.IdempotencyKey)
            .HasConversion(key => key.Value, value => IdempotencyKey.Create(value).Value)
            .HasColumnName("IdempotencyKey")
            .HasMaxLength(200)
            .IsRequired();

        // A diferencia de PaymentApp (único global), acá es único por tenant — cada tenant
        // tiene su propio espacio de IdempotencyKey.
        builder
            .HasIndex(payment => new { payment.TenantId, payment.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_TenantPayments_TenantId_IdempotencyKey");

        builder.OwnsOne(
            payment => payment.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(payment => payment.TaxpayerId);

        builder.OwnsOne(
            payment => payment.Purpose,
            purpose =>
            {
                purpose
                    .Property(p => p.Kind)
                    .HasColumnName("PurposeKind")
                    .HasConversion<string>()
                    .HasMaxLength(30)
                    .IsRequired();
                purpose
                    .Property(p => p.ExternalReferenceId)
                    .HasColumnName("PurposeExternalReferenceId")
                    .HasMaxLength(200);
            }
        );

        builder.Property(payment => payment.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.OwnsOne(
            payment => payment.ExternalChargeReference,
            reference =>
            {
                reference
                    .Property(r => r.Provider)
                    .HasColumnName("ExternalChargeProvider")
                    .HasConversion<string>()
                    .HasMaxLength(30);
                reference.Property(r => r.Value).HasColumnName("ExternalChargeReference").HasMaxLength(200);
            }
        );

        builder
            .Property(payment => payment.ProviderChargeReferenceOnConnect)
            .HasColumnName("ProviderChargeReferenceOnConnect")
            .HasMaxLength(200);

        builder.OwnsOne(
            payment => payment.SplitPayment,
            split =>
            {
                split.Property(s => s.TenantAmountCents).HasColumnName("TenantAmountCents");
                split.Property(s => s.PlatformFeeAmountCents).HasColumnName("PlatformFeeAmountCents");
                split.Property(s => s.PlatformFeeReference).HasColumnName("PlatformFeeReference").HasMaxLength(200);
            }
        );

        builder
            .Property(payment => payment.StatementDescriptor)
            .HasConversion(descriptor => descriptor.Value, value => StatementDescriptor.Create(value).Value)
            .HasColumnName("StatementDescriptor")
            .HasMaxLength(22)
            .IsRequired();

        builder.Property(payment => payment.NextActionType).HasMaxLength(100);
        builder.Property(payment => payment.NextActionUrl).HasMaxLength(2000);
        builder.Property(payment => payment.FailureCode).HasMaxLength(100);
        builder.Property(payment => payment.FailureReason).HasMaxLength(1000);

        builder.Property(payment => payment.RowVersion).IsRowVersion();

        builder
            .HasIndex(payment => new { payment.TenantId, payment.Status })
            .HasDatabaseName("IX_TenantPayments_TenantId_Status");

        builder
            .HasIndex(payment => payment.NextRetryAtUtc)
            .HasFilter("[Status] = 'Failed' AND [NextRetryAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_TenantPayments_Status_NextRetry");

        builder
            .HasMany(payment => payment.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.TenantPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.TenantPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
