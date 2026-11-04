namespace TaxVision.Signature.Api.Requests;

/// <summary>
/// Cuerpo del PUT /admin/tenants/{tenantId}/signature-constraints: la excepción del tenant sobre su plan.
/// Cada campo nulo (u omitido) hereda del plan. Los canales van como nombres del enum; el controller los
/// convierte al bitmask. Sólo un PlatformAdmin con <c>signature.constraints.manage</c>.
/// </summary>
public sealed record UpdateSignaturePlanConstraintsBody(
    int? MaxDocumentsPerRequest = null,
    int? MaxSignersPerRequest = null,
    long? MaxAllowedPdfBytes = null,
    int? MaxAllowedPages = null,
    int? MinRetentionYears = null,
    bool? PurgeAllowed = null,
    IReadOnlyList<string>? AllowedChannels = null,
    int? MaxTokenExpirationHours = null
);
