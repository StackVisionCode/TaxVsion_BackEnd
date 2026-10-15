using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantBillingPreferenceConfiguration : IEntityTypeConfiguration<TenantBillingPreference>
{
    public void Configure(EntityTypeBuilder<TenantBillingPreference> builder)
    {
        builder.ToTable("TenantBillingPreferences");
        builder.HasKey(preference => preference.Id);

        builder.Property(preference => preference.TenantId).IsRequired();
        builder.Property(preference => preference.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(preference => preference.ChosenAtUtc).IsRequired();
        builder.Property(preference => preference.ChosenByUserId).IsRequired();
        builder.Property(preference => preference.ProviderCode).HasMaxLength(50);
        builder.Property(preference => preference.CreatedAtUtc).IsRequired();
        builder.Property(preference => preference.UpdatedAtUtc).IsRequired();

        // Una sola preferencia por tenant: la elección vigente de cómo quiere pagar.
        builder
            .HasIndex(preference => preference.TenantId)
            .IsUnique()
            .HasDatabaseName("UX_TenantBillingPreferences_TenantId");

        builder
            .HasMany(preference => preference.Mandates)
            .WithOne()
            .HasForeignKey(mandate => mandate.TenantBillingPreferenceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(preference => preference.Mandates).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
