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

        // SignaturePlanConstraints: owned value object controlado por la plataforma.
        builder.OwnsOne(
            settings => settings.PlanConstraints,
            plan =>
            {
                plan.Property(p => p.MaxAllowedPdfBytes)
                    .HasColumnName("Plan_MaxAllowedPdfBytes")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultMaxAllowedPdfBytes)
                    .IsRequired();
                plan.Property(p => p.MaxAllowedImageBytes)
                    .HasColumnName("Plan_MaxAllowedImageBytes")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultMaxAllowedImageBytes)
                    .IsRequired();
                plan.Property(p => p.MaxAllowedPages)
                    .HasColumnName("Plan_MaxAllowedPages")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultMaxAllowedPages)
                    .IsRequired();
                plan.Property(p => p.MinRetentionYears)
                    .HasColumnName("Plan_MinRetentionYears")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultMinRetentionYears)
                    .IsRequired();
                plan.Property(p => p.PurgeAllowed)
                    .HasColumnName("Plan_PurgeAllowed")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultPurgeAllowed)
                    .IsRequired();
                plan.Property(p => p.AllowedChannels)
                    .HasColumnName("Plan_AllowedChannels")
                    .HasConversion<int>()
                    .HasDefaultValue(SignaturePlanConstraints.DefaultAllowedChannels)
                    // Sentinel: cuando el valor CLR es None (0), EF usa el default de la DB.
                    // None nunca es válido (invariante de dominio), así que esto es seguro.
                    .HasSentinel(VerificationChannel.None)
                    .IsRequired();
                plan.Property(p => p.MaxTokenExpirationHours)
                    .HasColumnName("Plan_MaxTokenExpirationHours")
                    .HasDefaultValue(SignaturePlanConstraints.DefaultMaxTokenExpirationHours)
                    .IsRequired();
            }
        );
    }
}
