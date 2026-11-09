using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    public void Configure(EntityTypeBuilder<RefundRequest> builder)
    {
        builder.ToTable("RefundRequests");
        builder.HasKey(request => request.Id);

        // Id de la fábrica de dominio y cuelga de SaaSPayment._refundRequests (§49).
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.SaaSPaymentId).IsRequired();
        builder.Property(request => request.TenantId).IsRequired();
        builder.Property(request => request.RequestKey).HasMaxLength(150).IsRequired();
        builder.Property(request => request.ProviderIdempotencyKey).HasMaxLength(200).IsRequired();

        builder.OwnsOne(
            request => request.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(request => request.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(request => request.RequestedByUserId).IsRequired();
        builder.Property(request => request.RefundedCentsBefore).IsRequired();
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(request => request.ProviderRefundReference).HasMaxLength(200);
        builder.Property(request => request.FailureCode).HasMaxLength(200);
        builder.Property(request => request.CreatedAtUtc).IsRequired();

        builder
            .HasIndex(request => new { request.SaaSPaymentId, request.RequestKey })
            .HasDatabaseName("IX_RefundRequests_SaaSPaymentId_RequestKey");
    }
}
