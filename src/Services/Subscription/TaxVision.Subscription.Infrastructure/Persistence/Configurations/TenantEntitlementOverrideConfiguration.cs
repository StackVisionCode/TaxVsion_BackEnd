using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantEntitlementOverrideConfiguration : IEntityTypeConfiguration<TenantEntitlementOverride>
{
    public void Configure(EntityTypeBuilder<TenantEntitlementOverride> builder)
    {
        builder.ToTable("TenantEntitlementOverrides");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.TenantId).IsRequired();
        builder.Property(item => item.Key).HasMaxLength(100).IsRequired();
        builder.Property(item => item.ValueType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(item => item.Value).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(500).IsRequired();
        builder.Property(item => item.CreatedAtUtc).IsRequired();
        builder.Property(item => item.UpdatedAtUtc).IsRequired();

        // Una sola excepción por oficina y clave: la base de datos lo garantiza aunque dos admins guarden a la vez.
        builder
            .HasIndex(item => new { item.TenantId, item.Key })
            .IsUnique()
            .HasDatabaseName("UX_TenantEntitlementOverrides_TenantId_Key");

        // El job de vencimientos solo busca las vencidas que todavía no recalculó.
        builder
            .HasIndex(item => item.ExpiresAtUtc)
            .HasFilter("[ExpiresAtUtc] IS NOT NULL AND [ExpiryAppliedAtUtc] IS NULL")
            .HasDatabaseName("IX_TenantEntitlementOverrides_ExpiresAtUtc_Pending");
    }
}
