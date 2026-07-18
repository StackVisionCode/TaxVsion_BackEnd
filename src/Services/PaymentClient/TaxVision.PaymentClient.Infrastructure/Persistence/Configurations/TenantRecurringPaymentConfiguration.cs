using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.PaymentClient.Domain.Recurring;

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Configurations;

public sealed class TenantRecurringPaymentConfiguration : IEntityTypeConfiguration<TenantRecurringPayment>
{
    public void Configure(EntityTypeBuilder<TenantRecurringPayment> builder)
    {
        builder.ToTable("TenantRecurringPayments");
        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.TenantId).IsRequired();
        builder.Property(plan => plan.TaxpayerId).IsRequired();
        builder.Property(plan => plan.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(plan => plan.PaymentMethodReference).HasMaxLength(255).IsRequired();

        builder.OwnsOne(
            plan => plan.Amount,
            money =>
            {
                money.Property(m => m.AmountCents).HasColumnName("AmountCents").IsRequired();
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            }
        );

        builder.OwnsOne(
            plan => plan.Purpose,
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

        builder.Property(plan => plan.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(plan => plan.CustomIntervalDays);
        builder.Property(plan => plan.StartDate).IsRequired();
        builder.Property(plan => plan.EndDate);
        builder.Property(plan => plan.MaxExecutions);
        builder.Property(plan => plan.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(plan => plan.NextExecutionDate);
        builder.Property(plan => plan.ExecutionCount).IsRequired();
        builder.Property(plan => plan.FailureCount).IsRequired();

        var backoffsConverter = new ValueConverter<IReadOnlyList<TimeSpan>, string>(
            backoffs => string.Join(',', backoffs.Select(b => b.Ticks)),
            csv => ParseBackoffs(csv)
        );
        var backoffsComparer = new ValueComparer<IReadOnlyList<TimeSpan>>(
            (a, b) => (a ?? new List<TimeSpan>()).SequenceEqual(b ?? new List<TimeSpan>()),
            list => list.Aggregate(0, (hash, backoff) => HashCode.Combine(hash, backoff)),
            list => list.ToList()
        );

        builder.OwnsOne(
            plan => plan.RetryPolicy,
            retryPolicy =>
            {
                retryPolicy.Property(r => r.MaxFailures).HasColumnName("RetryMaxFailures").IsRequired();
                retryPolicy
                    .Property(r => r.Backoffs)
                    .HasColumnName("RetryBackoffs")
                    .HasConversion(backoffsConverter)
                    .HasMaxLength(500)
                    .Metadata.SetValueComparer(backoffsComparer);
            }
        );

        builder.Property(plan => plan.PlatformFeeAmountCents);
        builder.Property(plan => plan.PlatformFeeReference).HasMaxLength(200);

        builder.Property(plan => plan.CreatedAtUtc).IsRequired();
        builder.Property(plan => plan.UpdatedAtUtc).IsRequired();
        builder.Property(plan => plan.CreatedBy).IsRequired();
        builder.Property(plan => plan.UpdatedBy).IsRequired();
        builder.Property(plan => plan.RowVersion).IsRowVersion();

        builder
            .HasIndex(plan => new { plan.TenantId, plan.Status })
            .HasDatabaseName("IX_TenantRecurringPayments_TenantId_Status");

        builder
            .HasIndex(plan => new { plan.TenantId, plan.TaxpayerId })
            .HasDatabaseName("IX_TenantRecurringPayments_TenantId_TaxpayerId");

        builder
            .HasMany(plan => plan.Schedules)
            .WithOne()
            .HasForeignKey(schedule => schedule.TenantRecurringPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(plan => plan.Schedules).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder
            .HasMany(plan => plan.Executions)
            .WithOne()
            .HasForeignKey(execution => execution.TenantRecurringPaymentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(plan => plan.Executions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static List<TimeSpan> ParseBackoffs(string csv) =>
        string.IsNullOrEmpty(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => new TimeSpan(long.Parse(t))).ToList();
}
