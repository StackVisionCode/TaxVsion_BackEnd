using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.PaymentApp.Domain.PaymentMethods;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Configurations;

public sealed class OnboardingPaymentMethodOverrideConfiguration
    : IEntityTypeConfiguration<OnboardingPaymentMethodOverride>
{
    public void Configure(EntityTypeBuilder<OnboardingPaymentMethodOverride> builder)
    {
        builder.ToTable("OnboardingPaymentMethodOverrides");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderCode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Enabled).IsRequired();
        builder.Property(x => x.DisabledReason).HasMaxLength(200);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedByUserId).IsRequired();

        builder
            .HasIndex(x => new { x.ProviderCode, x.Method })
            .IsUnique()
            .HasDatabaseName("UX_OnboardingPaymentMethodOverrides_Provider_Method");
    }
}
