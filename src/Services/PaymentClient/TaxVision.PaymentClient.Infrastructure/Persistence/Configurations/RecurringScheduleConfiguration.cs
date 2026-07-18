using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class RecurringScheduleConfiguration : IEntityTypeConfiguration<RecurringSchedule>
{
    public void Configure(EntityTypeBuilder<RecurringSchedule> builder)
    {
        builder.ToTable("RecurringSchedules");
        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();

        builder.Property(schedule => schedule.TenantRecurringPaymentId).IsRequired();
        builder.Property(schedule => schedule.TenantId).IsRequired();
        builder.Property(schedule => schedule.ScheduledDate).IsRequired();
        builder.Property(schedule => schedule.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.OwnsOne(
            schedule => schedule.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.Property(schedule => schedule.TenantPaymentId);
        builder.Property(schedule => schedule.ProviderResponse).HasMaxLength(1000);
        builder.Property(schedule => schedule.RetryCount).IsRequired();
        builder.Property(schedule => schedule.NextRetryAtUtc);

        builder
            .HasIndex(schedule => new { schedule.Status, schedule.ScheduledDate })
            .HasDatabaseName("IX_RecurringSchedules_Status_ScheduledDate");

        builder
            .HasIndex(schedule => new { schedule.Status, schedule.NextRetryAtUtc })
            .HasDatabaseName("IX_RecurringSchedules_Status_NextRetryAtUtc");
    }
}
