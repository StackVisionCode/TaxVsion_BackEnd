using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantAddOnConfiguration : IEntityTypeConfiguration<TenantAddOn>
{
    public void Configure(EntityTypeBuilder<TenantAddOn> builder)
    {
        builder.ToTable("TenantAddOns");
        builder.HasKey(addOn => addOn.Id);

        builder.Property(addOn => addOn.TenantId).IsRequired();
        builder.Property(addOn => addOn.AddOnDefinitionId).IsRequired();
        builder.Property(addOn => addOn.AddOnCode).HasMaxLength(50).IsRequired();
        builder.Property(addOn => addOn.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(addOn => addOn.Quantity).IsRequired();
        builder.Property(addOn => addOn.BillingCycle).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(addOn => addOn.CancellationReason).HasMaxLength(500);
        builder.Property(addOn => addOn.SuspensionReason).HasMaxLength(500);

        builder.OwnsOne(addOn => addOn.UnitPrice, money =>
        {
            money.Property(m => m.Amount).HasColumnName("UnitPriceAmount").HasPrecision(18, 4).IsRequired();
            money.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3).IsRequired();
        });

        builder.HasIndex(addOn => new { addOn.TenantId, addOn.Status });

        builder.HasIndex(addOn => addOn.NextRenewalAtUtc)
            .HasFilter("[Status] = 'Active' AND [AutoRenew] = 1")
            .HasDatabaseName("IX_TenantAddOns_NextRenewalAtUtc");

        builder.HasMany(addOn => addOn.Renewals)
            .WithOne()
            .HasForeignKey(renewal => renewal.TenantAddOnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(addOn => addOn.Renewals).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
