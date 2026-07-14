using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("TenantSubscriptions");
        builder.HasKey(subscription => subscription.Id);

        builder.Property(subscription => subscription.TenantId).IsRequired();
        builder.Property(subscription => subscription.PlanId).IsRequired();
        builder.Property(subscription => subscription.PlanVersionId).IsRequired();
        builder.Property(subscription => subscription.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(subscription => subscription.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(subscription => subscription.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(subscription => subscription.CancellationReason).HasMaxLength(500);
        builder.Property(subscription => subscription.SuspensionReason).HasMaxLength(500);

        builder.HasIndex(subscription => subscription.TenantId)
            .IsUnique()
            .HasFilter("[Status] <> 'Cancelled' AND [Status] <> 'Expired'")
            .HasDatabaseName("UX_TenantSubscriptions_TenantId_Active");

        builder.HasIndex(subscription => subscription.NextRenewalAtUtc)
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName("IX_TenantSubscriptions_NextRenewalAtUtc");

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(subscription => subscription.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionPlanVersion>()
            .WithMany()
            .HasForeignKey(subscription => subscription.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(subscription => subscription.Renewals)
            .WithOne()
            .HasForeignKey(renewal => renewal.TenantSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(subscription => subscription.Renewals).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(subscription => subscription.PlanChangeRequests)
            .WithOne()
            .HasForeignKey(request => request.TenantSubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(subscription => subscription.PlanChangeRequests).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
