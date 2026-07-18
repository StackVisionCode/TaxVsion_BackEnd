using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Consents;

/// <summary>
/// Registro immutable de la aceptación del consent por un firmante concreto en una
/// solicitud concreta. Guarda el <b>texto exacto</b> del consent tal como se le mostró —
/// snapshot no-mutable — junto con el idioma, versión, IP y user agent. Es la evidencia
/// legal exigida por IRS §7216 y análogos: el auditor puede reconstruir 5 años después
/// qué firmó el cliente palabra por palabra.
///
/// <para>
/// Aggregate append-only: nunca se muta después de crearse. Si el firmante re-abre el
/// enlace y "acepta de nuevo", NO se sobreescribe — se crea otro <c>ConsentEvent</c>.
/// La política P-16 del diseño resuelve la ambigüedad tomando el más reciente para el
/// gate de <c>MarkSignerSigned</c>, pero la cadena queda íntegra.
/// </para>
/// </summary>
public sealed class ConsentEvent : TenantEntity
{
    public const int MaxLanguageLength = 2;
    public const int MaxIpLength = 45;
    public const int MaxUserAgentLength = 500;

    private ConsentEvent() { }

    public Guid SignatureRequestId { get; private set; }
    public Guid SignerId { get; private set; }

    /// <summary>Slug de la versión de consent mostrada (ej. <c>consent.v2.es.7216</c>).</summary>
    public string TextVersion { get; private set; } = default!;

    /// <summary>Idioma ISO 639-1 corto en el que se mostró el texto.</summary>
    public string TextLanguage { get; private set; } = default!;

    /// <summary>Texto tal cual se renderizó al firmante. Immutable — no se re-flow para el audit.</summary>
    public string TextSnapshot { get; private set; } = default!;

    /// <summary>SHA-256 del <see cref="TextSnapshot"/>. Permite indexar y detectar tampering local.</summary>
    public string TextHash { get; private set; } = default!;

    public string? ClientIp { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }

    public static Result<ConsentEvent> RecordAcceptance(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        string textVersion,
        string textLanguage,
        string textSnapshot,
        string textHash,
        string? clientIp,
        string? userAgent
    )
    {
        var validation = ValidateFactoryInputs(
            tenantId,
            signatureRequestId,
            signerId,
            textVersion,
            textLanguage,
            textSnapshot,
            textHash
        );
        if (validation.IsFailure)
            return Result.Failure<ConsentEvent>(validation.Error);

        var record = new ConsentEvent
        {
            Id = Guid.NewGuid(),
            SignatureRequestId = signatureRequestId,
            SignerId = signerId,
            TextVersion = textVersion.Trim(),
            TextLanguage = textLanguage.Trim(),
            TextSnapshot = textSnapshot,
            TextHash = textHash.ToLowerInvariant(),
            ClientIp = TruncateIp(clientIp),
            UserAgent = TruncateUserAgent(userAgent),
            AcceptedAtUtc = DateTime.UtcNow,
        };
        record.SetTenant(tenantId);
        return Result.Success(record);
    }

    // ------------------------------------------------------------------
    // Helpers privados: una responsabilidad por método
    // ------------------------------------------------------------------

    private static Result ValidateFactoryInputs(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        string textVersion,
        string textLanguage,
        string textSnapshot,
        string textHash
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure(new Error("Signature.Consent.Tenant", "TenantId is required."));
        if (signatureRequestId == Guid.Empty)
            return Result.Failure(new Error("Signature.Consent.Request", "SignatureRequestId is required."));
        if (signerId == Guid.Empty)
            return Result.Failure(new Error("Signature.Consent.Signer", "SignerId is required."));
        if (string.IsNullOrWhiteSpace(textVersion))
            return Result.Failure(new Error("Signature.Consent.TextVersion", "TextVersion is required."));
        if (string.IsNullOrWhiteSpace(textLanguage) || textLanguage.Trim().Length > MaxLanguageLength)
            return Result.Failure(
                new Error("Signature.Consent.TextLanguage", "TextLanguage must be a short ISO 639-1 code.")
            );
        if (string.IsNullOrWhiteSpace(textSnapshot))
            return Result.Failure(new Error("Signature.Consent.TextSnapshot", "TextSnapshot is required."));
        if (string.IsNullOrWhiteSpace(textHash) || textHash.Length != 64)
            return Result.Failure(new Error("Signature.Consent.TextHash", "TextHash must be a 64-char SHA-256."));
        return Result.Success();
    }

    private static string? TruncateIp(string? ip) =>
        string.IsNullOrWhiteSpace(ip) ? null : (ip.Length > MaxIpLength ? ip[..MaxIpLength] : ip.Trim());

    private static string? TruncateUserAgent(string? ua) =>
        string.IsNullOrWhiteSpace(ua) ? null : (ua.Length > MaxUserAgentLength ? ua[..MaxUserAgentLength] : ua.Trim());
}
