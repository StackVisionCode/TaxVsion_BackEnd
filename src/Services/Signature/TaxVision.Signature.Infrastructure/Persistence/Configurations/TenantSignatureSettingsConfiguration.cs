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
        builder.Property(settings => settings.DefaultReminderIntervalHoursValue).IsRequired();
        builder.Property(settings => settings.GenerateCertificateByDefault).IsRequired();
        // Default true en la BD: los tenants existentes conservan el comportamiento actual (firma propia permitida).
        builder.Property(settings => settings.AllowEmployeeOwnSignature).HasDefaultValue(true).IsRequired();

        // F7 — defaults por tenant.
        builder.Property(settings => settings.SendPartialCopyDefault).HasDefaultValue(false).IsRequired();
        builder
            .Property(settings => settings.PartialCopyDefaultAudienceKind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(Domain.Requests.ValueObjects.PartialCopyAudienceKind.All)
            .IsRequired();
        // Las filas existentes ya se rellenaron en ON al crear la columna; el default de la BD es false para
        // que el false del dominio no se pierda (EF omite el valor CLR por defecto y aplicaría el de la BD).
        builder.Property(settings => settings.SendSealedDocumentDefault).HasDefaultValue(false).IsRequired();
        builder.Property(settings => settings.SendCertificateToSignersDefault).HasDefaultValue(false).IsRequired();
        // Consentimiento ESIGN/UETA: tenants existentes se backfillean en ON (política legal por default).
        builder.Property(settings => settings.RequiresConsentByDefault).HasDefaultValue(true).IsRequired();
        builder.Property(settings => settings.ExpirationEnabledByDefault).HasDefaultValue(true).IsRequired();
        // Estampado. Sin HasDefaultValue: el valor del dominio siempre se inserta; la migración rellena lo existente.
        builder.Property(settings => settings.ShowSignatureFrame).IsRequired();
        builder.Property(settings => settings.DateStampFormat).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(settings => settings.AuditSecretEncrypted).HasMaxLength(512).IsRequired();
        builder.Property(settings => settings.AuditKeyVersion).IsRequired();

        builder.Property(settings => settings.CreatedAtUtc).IsRequired();
        builder.Property(settings => settings.UpdatedAtUtc).IsRequired();
        builder.Property(settings => settings.RowVersion).IsRowVersion();

        // DocumentLimits: owned value object (columnas propias sin tabla separada).
        builder.OwnsOne(
            settings => settings.DocumentLimits,
            limits =>
            {
                limits.Property(l => l.MaxPdfBytes).HasColumnName("Limits_MaxPdfBytes").IsRequired();
                limits.Property(l => l.MaxPagesPerDocument).HasColumnName("Limits_MaxPagesPerDocument").IsRequired();
            }
        );

        // DeliveryPolicy: owned value object; los defaults de columna cubren las filas existentes.
        builder.OwnsOne(
            settings => settings.Delivery,
            delivery =>
            {
                delivery
                    .Property(d => d.LinkLifetimeDays)
                    .HasColumnName("Delivery_LinkLifetimeDays")
                    .HasDefaultValue(DeliveryPolicy.DefaultLinkLifetimeDays)
                    .IsRequired();
                delivery
                    .Property(d => d.AllowPublicReissue)
                    .HasColumnName("Delivery_AllowPublicReissue")
                    .HasDefaultValue(true)
                    .IsRequired();
                delivery
                    .Property(d => d.AttachDocuments)
                    .HasColumnName("Delivery_AttachDocuments")
                    .HasDefaultValue(false)
                    .IsRequired();
                delivery
                    .Property(d => d.MaxAttachmentBytes)
                    .HasColumnName("Delivery_MaxAttachmentBytes")
                    .HasDefaultValue(DeliveryPolicy.DefaultMaxAttachmentBytes)
                    .IsRequired();
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

        // Retención de la oficina proyectada desde Tenant (F4); null = no la fijó.
        builder.Property(settings => settings.OfficeRetentionYears).HasColumnName("OfficeRetention_Years");
        builder
            .Property(settings => settings.OfficeRetentionUpdatedAtUtc)
            .HasColumnName("OfficeRetention_UpdatedAtUtc");

        // SignaturePlanConstraints: excepción de PlatformAdmin; cada columna nula hereda del plan.
        builder.OwnsOne(
            settings => settings.PlanConstraints,
            plan =>
            {
                plan.Property(p => p.MaxDocumentsPerRequest).HasColumnName("Plan_MaxDocumentsPerRequest");
                plan.Property(p => p.MaxSignersPerRequest).HasColumnName("Plan_MaxSignersPerRequest");
                plan.Property(p => p.MaxAllowedPdfBytes).HasColumnName("Plan_MaxAllowedPdfBytes");
                plan.Property(p => p.MaxAllowedPages).HasColumnName("Plan_MaxAllowedPages");
                plan.Property(p => p.MinRetentionYears).HasColumnName("Plan_MinRetentionYears");
                plan.Property(p => p.PurgeAllowed).HasColumnName("Plan_PurgeAllowed");
                plan.Property(p => p.AllowedChannels).HasColumnName("Plan_AllowedChannels").HasConversion<int?>();
                plan.Property(p => p.MaxTokenExpirationHours).HasColumnName("Plan_MaxTokenExpirationHours");
            }
        );
        // Sin esto, una excepción con todo nulo se leería como null en vez de "sin excepción".
        builder.Navigation(settings => settings.PlanConstraints).IsRequired();
    }
}
