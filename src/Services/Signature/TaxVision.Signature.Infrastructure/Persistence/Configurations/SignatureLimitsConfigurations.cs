using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Platform;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TenantSignatureLimitsConfiguration : IEntityTypeConfiguration<TenantSignatureLimits>
{
    public void Configure(EntityTypeBuilder<TenantSignatureLimits> builder)
    {
        builder.ToTable("TenantSignatureLimits");
        builder.HasKey(limits => limits.Id);
        builder.Property(limits => limits.TenantId).IsRequired();
        builder.Property(limits => limits.PlanCode).HasMaxLength(100).IsRequired();
        builder.Property(limits => limits.AllowedChannels).HasConversion<int>().IsRequired();
        builder.Property(limits => limits.UpdatedAtUtc).IsRequired();
        builder.HasIndex(limits => limits.TenantId).IsUnique();
    }
}

// Fila única de la plataforma (sin tenant); la migración siembra SignaturePlatformSettings.Defaults().
public sealed class SignaturePlatformSettingsConfiguration : IEntityTypeConfiguration<SignaturePlatformSettings>
{
    public void Configure(EntityTypeBuilder<SignaturePlatformSettings> builder)
    {
        builder.ToTable("SignaturePlatformSettings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Ignore(settings => settings.Verification);
        builder.Property(settings => settings.UpdatedAtUtc).IsRequired();
    }
}
