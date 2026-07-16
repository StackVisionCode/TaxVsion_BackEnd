using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class RecurringPaymentExecutionConfiguration : IEntityTypeConfiguration<RecurringPaymentExecution>
{
    public void Configure(EntityTypeBuilder<RecurringPaymentExecution> builder)
    {
        builder.ToTable("RecurringPaymentExecutions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.Id).ValueGeneratedNever();

        builder.Property(execution => execution.TenantRecurringPaymentId).IsRequired();
        builder.Property(execution => execution.RecurringScheduleId).IsRequired();
        builder.Property(execution => execution.TenantId).IsRequired();
        builder.Property(execution => execution.ExecutedAtUtc).IsRequired();

        builder.OwnsOne(execution => execution.AmountCharged, money =>
        {
            money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.Property(execution => execution.Succeeded).IsRequired();
        builder.Property(execution => execution.ProviderResponse).HasMaxLength(1000);

        builder.HasIndex(execution => execution.RecurringScheduleId)
            .HasDatabaseName("IX_RecurringPaymentExecutions_RecurringScheduleId");
    }
}
