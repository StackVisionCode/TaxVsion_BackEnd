namespace TaxVision.Signature.Api.Requests;

/// <summary>
/// Cuerpo del PUT /admin/tenants/{tenantId}/signature-constraints.
/// Los canales se pasan como lista de nombres de enum para que el JSON sea legible;
/// el controller los convierte al bitmask antes de despachar.
/// Sólo puede invocar este endpoint un PlatformAdmin con el claim
/// <c>signature.constraints.manage</c>.
/// </summary>
public sealed record UpdateSignaturePlanConstraintsBody(
    long MaxAllowedPdfBytes,
    long MaxAllowedImageBytes,
    int MaxAllowedPages,
    int MinRetentionYears,
    bool PurgeAllowed,
    IReadOnlyList<string> AllowedChannels,
    int MaxTokenExpirationHours
);
