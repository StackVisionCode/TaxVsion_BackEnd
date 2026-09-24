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
    RetentionPolicyBody RetentionPolicy,
    // Default 48h (cada 2 días); opcional para retro-compat con clientes que no lo envían.
    int DefaultReminderIntervalHours = 48,
    // Gobernanza de firma del preparador (My Signature); opcional, default true (norma industria).
    bool AllowEmployeeOwnSignature = true
);

public sealed record DocumentLimitsBody(long MaxPdfBytes, long MaxImageBytes, int MaxPagesPerDocument);

public sealed record RetentionPolicyBody(int RetentionYears, bool AllowPurge);
