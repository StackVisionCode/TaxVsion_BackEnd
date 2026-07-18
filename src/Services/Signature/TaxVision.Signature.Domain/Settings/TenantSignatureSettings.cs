using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Configuración por tenant del microservicio Signature. Se crea al recibir el evento
/// <c>TenantCreatedIntegrationEvent</c> con valores por defecto. Es un aggregate root
/// singleton por tenant (unique index en TenantId).
///
/// Estado inicial:
/// <list type="bullet">
///   <item>Canales verificación: Email + SMS (Fase 1 de multicanal).</item>
///   <item>Canal por defecto: Email.</item>
///   <item>Retención: 7 años, purge deshabilitada.</item>
///   <item>Límites de documento: 25 MB PDF / 10 MB imagen / 100 páginas.</item>
///   <item>Secreto HMAC del audit trail: generado al crear (cifrado con
///     <c>ISecretProtector</c> en Infrastructure antes de persistir).</item>
/// </list>
/// </summary>
/// <remarks>
/// Cada mutación tiene su propio método con nombre explícito y su propia validación.
/// No hay un <c>Update(patch)</c> genérico: eso oculta reglas y rompe SRP.
/// </remarks>
public sealed class TenantSignatureSettings : BaseEntity
{
    public const VerificationChannel DefaultAllowedChannels = VerificationChannel.Email | VerificationChannel.Sms;
    public const VerificationChannel DefaultPreselectedChannel = VerificationChannel.Email;
    public const int DefaultTokenExpirationHours = 168; // 7 días
    public const int MinTokenExpirationHours = 1;
    public const int MaxTokenExpirationHours = 720; // 30 días

    private TenantSignatureSettings() { }

    /// <summary>Tenant dueño de la configuración. Único por tenant.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Canales de verificación habilitados para este tenant (bitmask).</summary>
    public VerificationChannel AllowedVerificationChannels { get; private set; }

    /// <summary>Canal preseleccionado para el firmante (debe estar dentro de AllowedVerificationChannels).</summary>
    public VerificationChannel DefaultVerificationChannel { get; private set; }

    /// <summary>Duración del token público del firmante al crear una solicitud.</summary>
    public int DefaultTokenExpirationHoursValue { get; private set; }

    /// <summary>Si se generan recordatorios automáticos por default en cada solicitud nueva.</summary>
    public bool RemindersEnabledByDefault { get; private set; }

    /// <summary>Si se genera el Certificate of Completion por default en cada solicitud nueva.</summary>
    public bool GenerateCertificateByDefault { get; private set; }

    /// <summary>Límites de documento (tamaño, páginas). Value Object inmutable.</summary>
    public DocumentLimits DocumentLimits { get; private set; } = default!;

    /// <summary>Política de retención (años, purge). Value Object inmutable.</summary>
    public RetentionPolicy Retention { get; private set; } = default!;

    /// <summary>
    /// Techos que la plataforma impone sobre la configuración que este tenant puede elegir.
    /// Actualizado por PlatformAdmin vía PUT /admin/tenants/{id}/signature-constraints.
    /// Nunca nulo: se inicializa con <see cref="SignaturePlanConstraints.Default()"/> al crear el tenant.
    /// </summary>
    public SignaturePlanConstraints PlanConstraints { get; private set; } = default!;

    /// <summary>
    /// Secreto HMAC per-tenant cifrado con AES-GCM. Alimenta la cadena de auditoría
    /// tamper-evident. Nunca se expone en responses.
    /// </summary>
    public string AuditSecretEncrypted { get; private set; } = default!;

    /// <summary>Versión de la clave del audit HMAC (soporta rotación trimestral).</summary>
    public int AuditKeyVersion { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    // ------------------------------------------------------------------
    // Factory
    // ------------------------------------------------------------------

    /// <summary>
    /// Crea la configuración inicial de un tenant con los defaults del ecosistema.
    /// El <paramref name="auditSecretEncrypted"/> lo produce Infrastructure con
    /// <c>ISecretProtector.Protect(RandomBytes(32))</c> antes de invocar esta factory.
    /// </summary>
    public static Result<TenantSignatureSettings> CreateForNewTenant(Guid tenantId, string auditSecretEncrypted)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantSignatureSettings>(
                new Error("Signature.Settings.Tenant", "TenantId is required.")
            );

        if (string.IsNullOrWhiteSpace(auditSecretEncrypted))
            return Result.Failure<TenantSignatureSettings>(
                new Error("Signature.Settings.AuditSecret", "Encrypted audit secret is required.")
            );

        var now = DateTime.UtcNow;
        return Result.Success(
            new TenantSignatureSettings
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AllowedVerificationChannels = DefaultAllowedChannels,
                DefaultVerificationChannel = DefaultPreselectedChannel,
                DefaultTokenExpirationHoursValue = DefaultTokenExpirationHours,
                RemindersEnabledByDefault = true,
                GenerateCertificateByDefault = true,
                DocumentLimits = DocumentLimits.Default(),
                Retention = RetentionPolicy.Default(),
                PlanConstraints = SignaturePlanConstraints.Default(),
                AuditSecretEncrypted = auditSecretEncrypted,
                AuditKeyVersion = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    // ------------------------------------------------------------------
    // Mutaciones: una por regla, sin genéricos "Update(patch)".
    // ------------------------------------------------------------------

    public Result AllowVerificationChannel(VerificationChannel channel)
    {
        if (channel == VerificationChannel.None)
            return Result.Failure(new Error("Signature.Settings.Channel", "Cannot allow the None channel."));

        AllowedVerificationChannels |= channel;
        Touch();
        return Result.Success();
    }

    public Result DisallowVerificationChannel(VerificationChannel channel)
    {
        if (!AllowedVerificationChannels.HasFlag(channel))
            return Result.Success();

        var remaining = AllowedVerificationChannels & ~channel;
        if (remaining == VerificationChannel.None)
            return Result.Failure(
                new Error(
                    "Signature.Settings.AtLeastOneChannel",
                    "At least one verification channel must remain enabled."
                )
            );

        AllowedVerificationChannels = remaining;

        // Si dejamos sin canal por defecto, se recalcula al primero disponible.
        if (!AllowedVerificationChannels.HasFlag(DefaultVerificationChannel))
            DefaultVerificationChannel = FirstEnabled(AllowedVerificationChannels);

        Touch();
        return Result.Success();
    }

    public Result SetDefaultVerificationChannel(VerificationChannel channel)
    {
        if (channel == VerificationChannel.None)
            return Result.Failure(new Error("Signature.Settings.Channel", "Default channel cannot be None."));

        if (!AllowedVerificationChannels.HasFlag(channel))
            return Result.Failure(
                new Error(
                    "Signature.Settings.ChannelNotAllowed",
                    "The default channel must be one of the allowed channels."
                )
            );

        DefaultVerificationChannel = channel;
        Touch();
        return Result.Success();
    }

    public Result ChangeDefaultTokenExpiration(int hours)
    {
        if (hours is < MinTokenExpirationHours or > MaxTokenExpirationHours)
            return Result.Failure(
                new Error(
                    "Signature.Settings.TokenExpiration",
                    "DefaultTokenExpirationHours must be between 1 and 720."
                )
            );

        DefaultTokenExpirationHoursValue = hours;
        Touch();
        return Result.Success();
    }

    public void EnableAutomaticReminders()
    {
        if (RemindersEnabledByDefault)
            return;

        RemindersEnabledByDefault = true;
        Touch();
    }

    public void DisableAutomaticReminders()
    {
        if (!RemindersEnabledByDefault)
            return;

        RemindersEnabledByDefault = false;
        Touch();
    }

    public void EnableCertificateOfCompletion()
    {
        if (GenerateCertificateByDefault)
            return;

        GenerateCertificateByDefault = true;
        Touch();
    }

    public void DisableCertificateOfCompletion()
    {
        if (!GenerateCertificateByDefault)
            return;

        GenerateCertificateByDefault = false;
        Touch();
    }

    public Result ReplaceDocumentLimits(DocumentLimits newLimits)
    {
        ArgumentNullException.ThrowIfNull(newLimits);
        DocumentLimits = newLimits;
        Touch();
        return Result.Success();
    }

    public Result ReplaceRetentionPolicy(RetentionPolicy newPolicy)
    {
        ArgumentNullException.ThrowIfNull(newPolicy);
        Retention = newPolicy;
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Aplica nuevas restricciones de plan y auto-corrige la configuración del tenant
    /// para que no exceda los techos. Llamado por PlatformAdmin.
    ///
    /// Auto-corrección:
    /// - canales que ya no están en el plan → se deshabilitan (manteniendo al menos uno).
    /// - DefaultVerificationChannel → se recalcula si quedó fuera del nuevo set permitido.
    /// - TokenExpiration → se clampea al nuevo máximo.
    /// - DocumentLimits → se clampean a los nuevos techos.
    /// - RetentionYears → se eleva al nuevo mínimo si estaba por debajo.
    /// - AllowPurge → se deshabilita si el plan ya no lo permite.
    /// </summary>
    public Result ApplyPlanConstraints(SignaturePlanConstraints newConstraints)
    {
        ArgumentNullException.ThrowIfNull(newConstraints);

        PlanConstraints = newConstraints;

        // 1. Canales: quitar los que ya no están en el plan.
        var validChannels = AllowedVerificationChannels & newConstraints.AllowedChannels;
        if (validChannels == VerificationChannel.None)
            validChannels = newConstraints.AllowedChannels; // fuerza al menos los del plan

        AllowedVerificationChannels = validChannels;

        if (!AllowedVerificationChannels.HasFlag(DefaultVerificationChannel))
            DefaultVerificationChannel = FirstEnabled(AllowedVerificationChannels);

        // 2. Token expiration.
        if (DefaultTokenExpirationHoursValue > newConstraints.MaxTokenExpirationHours)
            DefaultTokenExpirationHoursValue = newConstraints.MaxTokenExpirationHours;

        // 3. Document limits (clampear a los nuevos techos del plan).
        var clampedPdf = Math.Min(DocumentLimits.MaxPdfBytes, newConstraints.MaxAllowedPdfBytes);
        var clampedImage = Math.Min(DocumentLimits.MaxImageBytes, newConstraints.MaxAllowedImageBytes);
        var clampedPages = Math.Min(DocumentLimits.MaxPagesPerDocument, newConstraints.MaxAllowedPages);

        var limitsResult = DocumentLimits.Default().WithMaxPdfBytes(clampedPdf);
        if (limitsResult.IsFailure)
            return limitsResult;

        var imgResult = limitsResult.Value.WithMaxImageBytes(clampedImage);
        if (imgResult.IsFailure)
            return imgResult;

        var pagesResult = imgResult.Value.WithMaxPages(clampedPages);
        if (pagesResult.IsFailure)
            return pagesResult;

        DocumentLimits = pagesResult.Value;

        // 4. Retention: elevar años si están por debajo del nuevo mínimo.
        var effectiveYears = Math.Max(Retention.RetentionYears, newConstraints.MinRetentionYears);
        var yearsResult = RetentionPolicy.Default().WithYears(effectiveYears);
        if (yearsResult.IsFailure)
            return yearsResult;

        // 5. Purge: deshabilitar si el plan ya no lo permite.
        var purge = Retention.AllowPurge && newConstraints.PurgeAllowed;
        Retention = purge ? yearsResult.Value.WithPurgeAllowed() : yearsResult.Value.WithPurgeBlocked();

        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Reemplaza el secreto de auditoría por uno nuevo cifrado y avanza la versión.
    /// Los eventos históricos siguen validándose contra su propia
    /// <c>KeyVersion</c>; sólo los eventos nuevos usan la actual.
    /// </summary>
    public Result RotateAuditSecret(string newAuditSecretEncrypted)
    {
        if (string.IsNullOrWhiteSpace(newAuditSecretEncrypted))
            return Result.Failure(new Error("Signature.Settings.AuditSecret", "Encrypted audit secret is required."));

        AuditSecretEncrypted = newAuditSecretEncrypted;
        AuditKeyVersion++;
        Touch();
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Helpers privados
    // ------------------------------------------------------------------

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static VerificationChannel FirstEnabled(VerificationChannel mask)
    {
        foreach (VerificationChannel candidate in Enum.GetValues<VerificationChannel>())
        {
            if (candidate == VerificationChannel.None)
                continue;
            if (mask.HasFlag(candidate))
                return candidate;
        }

        return VerificationChannel.Email;
    }
}
