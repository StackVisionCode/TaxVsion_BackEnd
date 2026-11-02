using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Settings;

/// <summary>
/// Techos vigentes del tenant (plan + excepción de PlatformAdmin) incluidos en
/// <see cref="TenantSignatureSettingsResponse"/> para que el frontend deshabilite lo que los exceda.
/// </summary>
public sealed record SignaturePlanConstraintsResponse(
    int MaxDocumentsPerRequest,
    int MaxSignersPerRequest,
    long MaxAllowedPdfBytes,
    int MaxAllowedPages,
    int MinRetentionYears,
    bool PurgeAllowed,
    IReadOnlyList<string> AllowedChannels,
    int MaxTokenExpirationHours
)
{
    public static SignaturePlanConstraintsResponse From(SignatureLimits ceilings) =>
        new(
            ceilings.MaxDocumentsPerRequest,
            ceilings.MaxSignersPerRequest,
            ceilings.MaxPdfBytes,
            ceilings.MaxPagesPerDocument,
            ceilings.MinRetentionYears,
            ceilings.PurgeAllowed,
            ChannelsAsStrings(ceilings.AllowedChannels),
            ceilings.MaxTokenHours
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
