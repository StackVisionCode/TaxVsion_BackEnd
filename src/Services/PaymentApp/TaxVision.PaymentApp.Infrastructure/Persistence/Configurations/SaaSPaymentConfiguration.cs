using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class SaaSPaymentConfiguration : IEntityTypeConfiguration<SaaSPayment>
{
    public void Configure(EntityTypeBuilder<SaaSPayment> builder)
    {
        builder.ToTable("SaaSPayments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.TenantId).IsRequired();

        builder
            .Property(payment => payment.IdempotencyKey)
            .HasConversion(key => key.Value, value => IdempotencyKey.Create(value).Value)
            .HasColumnName("IdempotencyKey")
            .HasMaxLength(200)
            .IsRequired();
        builder
            .HasIndex(payment => payment.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_SaaSPayments_IdempotencyKey");

        builder.OwnsOne(
            payment => payment.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(payment => payment.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(payment => payment.TargetAggregateId).IsRequired();
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
            .Property(payment => payment.StatementDescriptor)
            .HasConversion(descriptor => descriptor.Value, value => StatementDescriptor.Create(value).Value)
            .HasColumnName("StatementDescriptor")
            .HasMaxLength(22)
            .IsRequired();

        builder.Property(payment => payment.NextActionType).HasMaxLength(100);
        builder.Property(payment => payment.NextActionUrl).HasMaxLength(2000);
        builder.Property(payment => payment.FailureCode).HasMaxLength(100);
        builder.Property(payment => payment.FailureReason).HasMaxLength(1000);

        builder.Property(payment => payment.CodeReservationId);
        builder.Property(payment => payment.CodeReservationPaymentId);
        builder.Property(payment => payment.DiscountAmountCents);
        builder.Property(payment => payment.PromotionSnapshotHash).HasMaxLength(64);

        builder.Property(payment => payment.RowVersion).IsRowVersion();

        builder
            .HasIndex(payment => new { payment.TenantId, payment.Status })
            .HasDatabaseName("IX_SaaSPayments_TenantId_Status");

        builder
            .HasIndex(payment => payment.NextRetryAtUtc)
            .HasFilter("[Status] = 'Failed' AND [NextRetryAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_SaaSPayments_Status_NextRetry");

        builder
            .HasMany(payment => payment.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.SaaSPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.SaaSPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(payment => payment.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
