using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("SubscriptionPlans");
        builder.HasKey(plan => plan.Id);

        builder
            .Property(plan => plan.Code)
            .HasConversion(code => code.Value, value => PlanCode.Create(value).Value)
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(plan => plan.Code).IsUnique();

        builder.Property(plan => plan.Name).HasMaxLength(200).IsRequired();
        builder.Property(plan => plan.Description).HasMaxLength(2000).IsRequired();
        builder.Property(plan => plan.Tier).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(plan => plan.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder
            .HasMany(plan => plan.Versions)
            .WithOne()
            .HasForeignKey(version => version.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(plan => plan.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
