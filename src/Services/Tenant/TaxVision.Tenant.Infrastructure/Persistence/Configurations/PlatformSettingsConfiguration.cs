using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tenant.Domain.Platform;

namespace TaxVision.Tenant.Infrastructure.Persistence.Configurations;

public sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> b)
    {
        b.ToTable("PlatformSettings");
        b.HasKey(x => x.Id);
        // Id fijo (PlatformSettings.SingletonId): si EF lo tratara como generado, un Id ya puesto se
        // guardaría como UPDATE y la fila nunca se insertaría.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BrandName).HasMaxLength(PlatformSettings.MaxBrandNameLength).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(PlatformSettings.MaxLegalNameLength).IsRequired();
        b.Property(x => x.BaseDomain).HasMaxLength(PlatformSettings.MaxHostLength).IsRequired();
        b.Property(x => x.ApiHost).HasMaxLength(PlatformSettings.MaxHostLength).IsRequired();
        b.Property(x => x.SupportEmail).HasMaxLength(PlatformSettings.MaxEmailLength).IsRequired();
        b.Property(x => x.BillingEmail).HasMaxLength(PlatformSettings.MaxEmailLength).IsRequired();
        // Dos PUT simultáneos no pueden salir con la misma versión: el segundo falla en vez de pisar.
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
