using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class SaaSPaymentAttemptConfiguration : IEntityTypeConfiguration<SaaSPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<SaaSPaymentAttempt> builder)
    {
        builder.ToTable("SaaSPaymentAttempts");
        builder.HasKey(attempt => attempt.Id);

        // *** GUARDRAIL persistencia (§49) ***
        // Id se genera en la factory de dominio y la entidad cuelga de
        // SaaSPayment._attempts (HasMany) -> requiere ValueGeneratedNever().
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();

        builder.Property(attempt => attempt.SaaSPaymentId).IsRequired();
        builder.Property(attempt => attempt.TenantId).IsRequired();
        builder.Property(attempt => attempt.AttemptNumber).IsRequired();
        builder.Property(attempt => attempt.AttemptedAtUtc).IsRequired();
        builder.Property(attempt => attempt.ProviderResponseCode).HasMaxLength(100);
        builder.Property(attempt => attempt.ProviderResponseBody).HasColumnType("nvarchar(max)");

        builder.HasIndex(attempt => attempt.SaaSPaymentId).HasDatabaseName("IX_SaaSPaymentAttempts_SaaSPaymentId");
    }
}
