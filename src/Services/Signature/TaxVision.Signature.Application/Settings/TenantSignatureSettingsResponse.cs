using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings;

/// <summary>
/// Vista pública de <see cref="TenantSignatureSettings"/> para el staff. Omite
/// deliberadamente el secreto HMAC del audit trail (nunca se expone en respuestas ni
/// en logs, incluso cifrado).
/// </summary>
public sealed record TenantSignatureSettingsResponse(
    Guid TenantId,
    IReadOnlyList<string> AllowedVerificationChannels,
    string DefaultVerificationChannel,
    int DefaultTokenExpirationHours,
    bool RemindersEnabledByDefault,
    bool GenerateCertificateByDefault,
    long MaxPdfBytes,
    long MaxImageBytes,
    int MaxPagesPerDocument,
    int RetentionYears,
    bool AllowPurge,
    int AuditKeyVersion,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static TenantSignatureSettingsResponse From(TenantSignatureSettings settings) =>
        new(
            settings.TenantId,
            ChannelsAsStrings(settings.AllowedVerificationChannels),
            settings.DefaultVerificationChannel.ToString(),
            settings.DefaultTokenExpirationHoursValue,
            settings.RemindersEnabledByDefault,
            settings.GenerateCertificateByDefault,
            settings.DocumentLimits.MaxPdfBytes,
            settings.DocumentLimits.MaxImageBytes,
            settings.DocumentLimits.MaxPagesPerDocument,
            settings.Retention.RetentionYears,
            settings.Retention.AllowPurge,
            settings.AuditKeyVersion,
            settings.CreatedAtUtc,
            settings.UpdatedAtUtc
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
