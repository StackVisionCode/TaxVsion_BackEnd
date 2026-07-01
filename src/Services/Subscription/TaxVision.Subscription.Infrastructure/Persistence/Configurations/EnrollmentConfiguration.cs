using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Enrollments;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<SubscriptionEnrollment>
{
    public void Configure(EntityTypeBuilder<SubscriptionEnrollment> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Status);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.BillingPeriod).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.AdminEmail).HasMaxLength(256).IsRequired();
        builder.Property(e => e.OrgName).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Subdomain).HasMaxLength(50).IsRequired();
        builder.Property(e => e.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TimeZoneId).HasMaxLength(100);
    }
}
