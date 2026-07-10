using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class TenantSignatureSettingsConfiguration : IEntityTypeConfiguration<TenantSignatureSettings>
{
    public void Configure(EntityTypeBuilder<TenantSignatureSettings> builder)
    {
        builder.ToTable("TenantSignatureSettings");
        builder.HasKey(settings => settings.Id);

        builder.Property(settings => settings.TenantId).IsRequired();
        builder.HasIndex(settings => settings.TenantId).IsUnique();

        builder.Property(settings => settings.AllowedVerificationChannels).HasConversion<int>().IsRequired();
        builder.Property(settings => settings.DefaultVerificationChannel).HasConversion<int>().IsRequired();

        builder.Property(settings => settings.DefaultTokenExpirationHoursValue).IsRequired();
        builder.Property(settings => settings.RemindersEnabledByDefault).IsRequired();
        builder.Property(settings => settings.GenerateCertificateByDefault).IsRequired();

        builder.Property(settings => settings.AuditSecretEncrypted).HasMaxLength(512).IsRequired();
        builder.Property(settings => settings.AuditKeyVersion).IsRequired();

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.UpdatedAtUtc).IsRequired();

        // DocumentLimits: owned value object (columnas propias sin tabla separada).
        builder.OwnsOne(
            settings => settings.DocumentLimits,
            limits =>
            {
                limits.Property(l => l.MaxPdfBytes).HasColumnName("Limits_MaxPdfBytes").IsRequired();
                limits.Property(l => l.MaxImageBytes).HasColumnName("Limits_MaxImageBytes").IsRequired();
                limits.Property(l => l.MaxPagesPerDocument).HasColumnName("Limits_MaxPagesPerDocument").IsRequired();
            }
        );

        // RetentionPolicy: owned value object.
        builder.OwnsOne(
            settings => settings.Retention,
            retention =>
            {
                retention.Property(r => r.RetentionYears).HasColumnName("Retention_Years").IsRequired();
                retention.Property(r => r.AllowPurge).HasColumnName("Retention_AllowPurge").IsRequired();
            }
        );
    }
}
