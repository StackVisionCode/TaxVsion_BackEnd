using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings;

/// <summary>
/// Proyección de <see cref="SignaturePlanConstraints"/> incluida en
/// <see cref="TenantSignatureSettingsResponse"/> para que el frontend conozca
/// los techos del plan y deshabilite opciones que los excedan.
/// </summary>
public sealed record SignaturePlanConstraintsResponse(
    long MaxAllowedPdfBytes,
    long MaxAllowedImageBytes,
    int MaxAllowedPages,
    int MinRetentionYears,
    bool PurgeAllowed,
    IReadOnlyList<string> AllowedChannels,
    int MaxTokenExpirationHours
)
{
    public static SignaturePlanConstraintsResponse From(SignaturePlanConstraints constraints) =>
        new(
            constraints.MaxAllowedPdfBytes,
            constraints.MaxAllowedImageBytes,
            constraints.MaxAllowedPages,
            constraints.MinRetentionYears,
            constraints.PurgeAllowed,
            ChannelsAsStrings(constraints.AllowedChannels),
            constraints.MaxTokenExpirationHours
        );

    private static IReadOnlyList<string> ChannelsAsStrings(VerificationChannel mask)
    {
        var result = new List<string>();
        foreach (VerificationChannel ch in Enum.GetValues<VerificationChannel>())
        {
            if (ch == VerificationChannel.None)
                continue;
            if (mask.HasFlag(ch))
                result.Add(ch.ToString());
        }
        return result;
    }
}
