using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings;

/// <summary>
/// Vista pública de <see cref="TenantSignatureSettings"/> para el staff. Omite
/// deliberadamente el secreto HMAC del audit trail (nunca se expone en respuestas ni
/// en logs, incluso cifrado).
///
/// Incluye <see cref="PlanConstraints"/> (techos vigentes: plan + excepción) para que el frontend
/// deshabilite opciones que los excedan.
///
/// <see cref="RetentionYears"/> es la retención efectiva (Cumplimiento ?? Signature, nunca bajo el mínimo del
/// plan) y <see cref="RetentionYearsSource"/> dice de dónde sale ("Office" | "Signature").
/// </summary>
public sealed record TenantSignatureSettingsResponse(
    Guid TenantId,
    IReadOnlyList<string> AllowedVerificationChannels,
    string DefaultVerificationChannel,
    int DefaultTokenExpirationHours,
    bool RemindersEnabledByDefault,
    int DefaultReminderIntervalHours,
    bool GenerateCertificateByDefault,
    bool AllowEmployeeOwnSignature,
    long MaxPdfBytes,
    int MaxPagesPerDocument,
    int RetentionYears,
    bool AllowPurge,
    int AuditKeyVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    SignaturePlanConstraintsResponse PlanConstraints,
    // F7 — defaults de entrega + expiración opcional.
    bool SendPartialCopyDefault,
    string PartialCopyDefaultAudienceKind,
    bool SendSealedDocumentDefault,
    bool ExpirationEnabledByDefault,
    bool SendCertificateToSignersDefault,
    bool RequiresConsentByDefault,
    DeliveryPolicyResponse Delivery,
    bool ShowSignatureFrame,
    string DateStampFormat,
    string RetentionYearsSource
)
{
    public static TenantSignatureSettingsResponse From(
        TenantSignatureSettings settings,
        EffectiveSignaturePolicy policy
    ) =>
        new(
            settings.TenantId,
            ChannelsAsStrings(settings.AllowedVerificationChannels),
            settings.DefaultVerificationChannel.ToString(),
            settings.DefaultTokenExpirationHoursValue,
            settings.RemindersEnabledByDefault,
            settings.DefaultReminderIntervalHoursValue,
            settings.GenerateCertificateByDefault,
            settings.AllowEmployeeOwnSignature,
            settings.DocumentLimits.MaxPdfBytes,
            settings.DocumentLimits.MaxPagesPerDocument,
            policy.RetentionYears,
            settings.Retention.AllowPurge,
            settings.AuditKeyVersion,
            settings.CreatedAtUtc,
            settings.UpdatedAtUtc,
            SignaturePlanConstraintsResponse.From(policy.Ceilings),
            settings.SendPartialCopyDefault,
            settings.PartialCopyDefaultAudienceKind.ToString(),
            settings.SendSealedDocumentDefault,
            settings.ExpirationEnabledByDefault,
            settings.SendCertificateToSignersDefault,
            settings.RequiresConsentByDefault,
            new DeliveryPolicyResponse(
                settings.Delivery.LinkLifetimeDays,
                settings.Delivery.AllowPublicReissue,
                settings.Delivery.AttachDocuments,
                settings.Delivery.MaxAttachmentBytes
            ),
            settings.ShowSignatureFrame,
            settings.DateStampFormat.ToString(),
            policy.RetentionSource.ToString()
        );

    private static IReadOnlyList<string> ChannelsAsStrings(VerificationChannel mask)
    {
        var result = new List<string>();
        foreach (VerificationChannel candidate in Enum.GetValues<VerificationChannel>())
        {
            if (candidate == VerificationChannel.None)
                continue;
            if (mask.HasFlag(candidate))
                result.Add(candidate.ToString());
        }
        return result;
    }
}
