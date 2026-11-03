using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Billing.Domain.Settings;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Ajustes de facturación por tenant (plazo, instrucciones de pago, pie). Uno por tenant.</summary>
public sealed class TenantBillingSettingsConfiguration : IEntityTypeConfiguration<TenantBillingSettings>
{
    public void Configure(EntityTypeBuilder<TenantBillingSettings> b)
    {
        b.ToTable("TenantBillingSettings");
        b.HasKey(s => s.Id);

        b.Property(s => s.TenantId).IsRequired();
        b.HasIndex(s => s.TenantId).IsUnique();

        b.Property(s => s.PaymentTerms).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(s => s.PaymentInstructions).HasMaxLength(TenantBillingSettings.TextMaxLength);
        b.Property(s => s.InvoiceFooter).HasMaxLength(TenantBillingSettings.TextMaxLength);
        b.Property(s => s.RowVersion).IsRowVersion();
    }
}
