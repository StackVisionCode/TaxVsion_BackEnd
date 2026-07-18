namespace TaxVision.Signature.Api.Requests;

/// <summary>
/// Cuerpo del PUT /signature/settings. Reemplaza toda la configuración del tenant de
/// una vez (semántica PUT). Los canales se pasan como lista de nombres de enum para
/// que el JSON sea legible; el controller los convierte al bitmask antes de despachar.
/// </summary>
public sealed record UpdateSignatureSettingsBody(
    IReadOnlyList<string> AllowedVerificationChannels,
    string DefaultVerificationChannel,
    int DefaultTokenExpirationHours,
    bool RemindersEnabledByDefault,
    bool GenerateCertificateByDefault,
    DocumentLimitsBody DocumentLimits,
    RetentionPolicyBody RetentionPolicy
);

public sealed record DocumentLimitsBody(long MaxPdfBytes, long MaxImageBytes, int MaxPagesPerDocument);

public sealed record RetentionPolicyBody(int RetentionYears, bool AllowPurge);
