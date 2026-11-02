using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Signature.Domain.Requests.ValueObjects;

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
///   <item>Límites de documento: 25 MB PDF / 100 páginas (dentro de los techos del plan).</item>
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

    public const int DefaultReminderIntervalHours = 48; // cada 2 días
    public const int MinReminderIntervalHours = 1;
    public const int MaxReminderIntervalHours = 720; // 30 días

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

    /// <summary>Cada cuántas horas se recuerda a los firmantes pendientes, por default en solicitudes nuevas.</summary>
    public int DefaultReminderIntervalHoursValue { get; private set; }

    /// <summary>Si se genera el Certificate of Completion por default en cada solicitud nueva.</summary>
    public bool GenerateCertificateByDefault { get; private set; }

    /// <summary>
    /// Gobernanza de firmas del preparador (14.5 My Signature): si es true, cada empleado puede usar su
    /// propia firma; si es false, se fuerza la firma de oficina. Default true (norma de la industria).
    /// </summary>
    public bool AllowEmployeeOwnSignature { get; private set; }

    /// <summary>F7 — default por tenant: enviar la copia inmediata al firmar.</summary>
    public bool SendPartialCopyDefault { get; private set; }

    /// <summary>
    /// F7 — audiencia por defecto cuando el flag arriba está ON. Solo `Kind`; una lista específica
    /// no tiene sentido como default del tenant (los SignerIds dependen de cada request).
    /// </summary>
    public PartialCopyAudienceKind PartialCopyDefaultAudienceKind { get; private set; }

    /// <summary>F7 — default por tenant: enviar el PDF sellado final al completarse.</summary>
    public bool SendSealedDocumentDefault { get; private set; }

    /// <summary>
    /// Default por tenant: emailar el Certificate of Completion a los firmantes al completarse.
    /// Solo surte efecto si además se genera el certificado; apagado en tenants nuevos.
    /// </summary>
    public bool SendCertificateToSignersDefault { get; private set; }

    /// <summary>
    /// Política legal del despacho: las solicitudes nuevas exigen el consentimiento de firma electrónica
    /// (ESIGN/UETA) por default. ON en tenants nuevos — apagarlo debilita la exigibilidad de la firma.
    /// </summary>
    public bool RequiresConsentByDefault { get; private set; }

    /// <summary>F7 — default por tenant: las requests nuevas expiran (ON) o viven indefinido (OFF).</summary>
    public bool ExpirationEnabledByDefault { get; private set; }

    /// <summary>
    /// Marco alrededor de la firma en el PDF sellado ("Signed by", fecha/hora y referencia al certificado).
    /// Opcional como en la industria; las iniciales, fechas y textos nunca llevan marco.
    /// </summary>
    public bool ShowSignatureFrame { get; private set; }

    /// <summary>Formato con el que se estampa la fecha de firma en los campos Date.</summary>
    public DateStampFormat DateStampFormat { get; private set; }

    /// <summary>Límites de documento (tamaño, páginas). Value Object inmutable.</summary>
    public DocumentLimits DocumentLimits { get; private set; } = default!;

    /// <summary>Política de retención (años, purge). Value Object inmutable.</summary>
    public RetentionPolicy Retention { get; private set; } = default!;

    /// <summary>Entrega de los documentos firmados (vigencia del enlace, reemisión, adjuntos).</summary>
    public DeliveryPolicy Delivery { get; private set; } = default!;

    /// <summary>
    /// Excepción de PlatformAdmin sobre lo que vende el plan (PUT /admin/tenants/{id}/signature-constraints).
    /// Nunca nulo: sin excepción es <see cref="SignaturePlanConstraints.None()"/> y rige el plan.
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
                DefaultReminderIntervalHoursValue = DefaultReminderIntervalHours,
                GenerateCertificateByDefault = true,
                AllowEmployeeOwnSignature = true,
                // F7 — defaults de entrega: tenants nuevos arrancan con todo apagado salvo expiración.
                SendPartialCopyDefault = false,
                PartialCopyDefaultAudienceKind = PartialCopyAudienceKind.All,
                SendSealedDocumentDefault = false,
                SendCertificateToSignersDefault = false,
                // Consentimiento ESIGN/UETA: política legal del despacho, ON por default.
                RequiresConsentByDefault = true,
                ExpirationEnabledByDefault = true,
                ShowSignatureFrame = true,
                DateStampFormat = DateStampFormat.MonthDayYear,
                DocumentLimits = DocumentLimits.Default(),
                Retention = RetentionPolicy.Default(),
                Delivery = DeliveryPolicy.Default(),
                PlanConstraints = SignaturePlanConstraints.None(),
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

    /// <summary>La vida por defecto del enlace del firmante, dentro del techo de horas vigente.</summary>
    public Result ChangeDefaultTokenExpiration(int hours, SignatureLimits ceilings)
    {
        var allowed = ceilings.CheckTokenHours(hours);
        if (allowed.IsFailure)
            return allowed;

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

    /// <summary>F7 — enciende/apaga el default por tenant de la copia parcial.</summary>
    public void SetSendPartialCopyDefault(bool enabled)
    {
        if (SendPartialCopyDefault == enabled)
            return;
        SendPartialCopyDefault = enabled;
        Touch();
    }

    /// <summary>F7 — audiencia por defecto. Específica sin lista no tiene sentido como default; solo guardamos el Kind.</summary>
    public void SetPartialCopyDefaultAudienceKind(PartialCopyAudienceKind kind)
    {
        if (PartialCopyDefaultAudienceKind == kind)
            return;
        PartialCopyDefaultAudienceKind = kind;
        Touch();
    }

    /// <summary>F7 — enciende/apaga el default por tenant del PDF sellado final.</summary>
    public void SetSendSealedDocumentDefault(bool enabled)
    {
        if (SendSealedDocumentDefault == enabled)
            return;
        SendSealedDocumentDefault = enabled;
        Touch();
    }

    /// <summary>F7 — enciende/apaga el default por tenant de la expiración de nuevos links.</summary>
    public void SetExpirationEnabledByDefault(bool enabled)
    {
        if (ExpirationEnabledByDefault == enabled)
            return;
        ExpirationEnabledByDefault = enabled;
        Touch();
    }

    public void SetShowSignatureFrame(bool show)
    {
        if (ShowSignatureFrame == show)
            return;
        ShowSignatureFrame = show;
        Touch();
    }

    public void SetDateStampFormat(DateStampFormat format)
    {
        if (DateStampFormat == format)
            return;
        DateStampFormat = format;
        Touch();
    }

    /// <summary>Fija el intervalo por default (horas) entre recordatorios a firmantes pendientes.</summary>
    public Result SetDefaultReminderInterval(int hours)
    {
        if (hours is < MinReminderIntervalHours or > MaxReminderIntervalHours)
            return Result.Failure(
                new Error(
                    "Signature.Settings.ReminderInterval",
                    $"Reminder interval must be between {MinReminderIntervalHours} and {MaxReminderIntervalHours} hours."
                )
            );

        DefaultReminderIntervalHoursValue = hours;
        Touch();
        return Result.Success();
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

    public void SetSendCertificateToSignersDefault(bool enabled)
    {
        if (SendCertificateToSignersDefault == enabled)
            return;

        SendCertificateToSignersDefault = enabled;
        Touch();
    }

    public void SetRequiresConsentByDefault(bool required)
    {
        if (RequiresConsentByDefault == required)
            return;

        RequiresConsentByDefault = required;
        Touch();
    }

    /// <summary>Permite que cada empleado use su propia firma (My Signature).</summary>
    public void EnableEmployeeOwnSignature()
    {
        if (AllowEmployeeOwnSignature)
            return;

        AllowEmployeeOwnSignature = true;
        Touch();
    }

    /// <summary>Fuerza la firma de oficina: los empleados no usan su firma personal.</summary>
    public void DisableEmployeeOwnSignature()
    {
        if (!AllowEmployeeOwnSignature)
            return;

        AllowEmployeeOwnSignature = false;
        Touch();
    }

    public Result ReplaceDocumentLimits(DocumentLimits newLimits)
    {
        ArgumentNullException.ThrowIfNull(newLimits);
        DocumentLimits = newLimits;
        Touch();
        return Result.Success();
    }

    public Result ReplaceDeliveryPolicy(DeliveryPolicy newPolicy)
    {
        ArgumentNullException.ThrowIfNull(newPolicy);
        Delivery = newPolicy;
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
    /// Guarda la excepción de PlatformAdmin. No reescribe lo que eligió la oficina: si queda por encima de
    /// los techos nuevos, <see cref="EffectiveWithin"/> lo acota al leerlo (igual que si baja el plan).
    /// </summary>
    public void ApplyPlanConstraints(SignaturePlanConstraints newConstraints)
    {
        ArgumentNullException.ThrowIfNull(newConstraints);
        PlanConstraints = newConstraints;
        Touch();
    }

    /// <summary>Lo que eligió la oficina acotado a los techos vigentes (plan + excepción).</summary>
    public EffectiveSignaturePolicy EffectiveWithin(SignatureLimits planLimits)
    {
        var ceilings = PlanConstraints.ApplyTo(planLimits);

        var channels = AllowedVerificationChannels & ceilings.AllowedChannels;
        if (channels == VerificationChannel.None)
            channels = ceilings.AllowedChannels;
        var defaultChannel = channels.HasFlag(DefaultVerificationChannel)
            ? DefaultVerificationChannel
            : FirstEnabled(channels);

        var forRequests = SignatureLimits.Create(
            ceilings.MaxDocumentsPerRequest,
            ceilings.MaxSignersPerRequest,
            Math.Min(DocumentLimits.MaxPdfBytes, ceilings.MaxPdfBytes),
            Math.Min(DocumentLimits.MaxPagesPerDocument, ceilings.MaxPagesPerDocument),
            ceilings.MaxTokenHours,
            ceilings.MinRetentionYears,
            ceilings.PurgeAllowed,
            channels
        );

        return new EffectiveSignaturePolicy(
            ceilings,
            forRequests,
            Math.Min(DefaultTokenExpirationHoursValue, ceilings.MaxTokenHours),
            Math.Max(Retention.RetentionYears, ceilings.MinRetentionYears),
            Retention.AllowPurge && ceilings.PurgeAllowed,
            defaultChannel
        );
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
