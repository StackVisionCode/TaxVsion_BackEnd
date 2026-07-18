using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.TenantPayments;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantPaymentAttemptConfiguration : IEntityTypeConfiguration<TenantPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<TenantPaymentAttempt> builder)
    {
        builder.ToTable("TenantPaymentAttempts");
        builder.HasKey(attempt => attempt.Id);

        // *** GUARDRAIL persistencia (§49) ***
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();

        builder.Property(attempt => attempt.TenantPaymentId).IsRequired();
        builder.Property(attempt => attempt.TenantId).IsRequired();
        builder.Property(attempt => attempt.AttemptNumber).IsRequired();
        builder.Property(attempt => attempt.AttemptedAtUtc).IsRequired();
        builder.Property(attempt => attempt.ProviderResponseCode).HasMaxLength(100);
        builder.Property(attempt => attempt.ProviderResponseBody).HasColumnType("nvarchar(max)");

        builder.HasIndex(attempt => attempt.TenantPaymentId).HasDatabaseName("IX_TenantPaymentAttempts_TenantPaymentId");
    }
}
