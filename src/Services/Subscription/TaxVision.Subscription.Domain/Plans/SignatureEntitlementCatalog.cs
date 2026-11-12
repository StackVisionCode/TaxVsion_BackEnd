using System.Globalization;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Claves <c>signature.*</c>: techos de firma electrónica por plan. Las lee Signature del snapshot de
/// entitlements y reemplazan a las constantes de su código y a los techos que hoy se fijan tenant por
/// tenant. Los valores replican lo vigente (iguales para todos los planes): desplegar no cambia nada.
/// </summary>
public static class SignatureEntitlementCatalog
{
    public const string MaxDocumentsPerRequest = "signature.max_documents_per_request";
    public const string MaxSignersPerRequest = "signature.max_signers_per_request";
    public const string MaxPdfBytes = "signature.max_pdf_bytes";
    public const string MaxPagesPerDocument = "signature.max_pages_per_document";
    public const string MaxTokenHours = "signature.max_token_hours";
    public const string MinRetentionYears = "signature.min_retention_years";
    public const string PurgeAllowed = "signature.purge_allowed";

    /// <summary>Canales de verificación del firmante permitidos, separados por coma (<c>Email,Sms</c>).</summary>
    public const string Channels = "signature.channels";

    public static IReadOnlyList<string> Keys { get; } =
    [
        MaxDocumentsPerRequest,
        MaxSignersPerRequest,
        MaxPdfBytes,
        MaxPagesPerDocument,
        MaxTokenHours,
        MinRetentionYears,
        PurgeAllowed,
        Channels,
    ];

    public static IReadOnlyCollection<EntitlementSeed> Defaults { get; } =
    [
        Int(MaxDocumentsPerRequest, 20),
        Int(MaxSignersPerRequest, 50),
        new(MaxPdfBytes, EntitlementValueType.Long, (25L * 1024 * 1024).ToString(CultureInfo.InvariantCulture)),
        Int(MaxPagesPerDocument, 100),
        Int(MaxTokenHours, 720),
        Int(MinRetentionYears, 7),
        // "True"/"False": mismo formato que produce el recálculo para las features (bool.ToString()).
        new(PurgeAllowed, EntitlementValueType.Bool, false.ToString()),
        new(Channels, EntitlementValueType.String, "Email,Sms"),
    ];

    private static EntitlementSeed Int(string key, int value) =>
        new(key, EntitlementValueType.Int, value.ToString(CultureInfo.InvariantCulture));
}
