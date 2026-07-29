using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Terms;

/// <summary>
/// Registro inmutable (append-only) de que un usuario acepto, en nombre del tenant,
/// una version puntual del ToS/AUP. La version vigente exigida vive en TermsOptions;
/// este historial existe para poder probar, ante una disputa legal, que el tenant
/// acepto esa version especifica en un momento dado — nunca se actualiza in place.
///
/// PayFlow Fase 6 (retrofit): TermsVersionId enlaza con Onboarding.TermsVersions.TermsVersion
/// (Opcion C del plan). ContentHash es nullable a nivel de columna solo para las filas legacy
/// backfilleadas por la migracion de retrofit (no tenian un documento con hash rastreado) —
/// AcceptedInContext distingue ese caso ("LegacyPreV2") de las aceptaciones nuevas, que si
/// deberian llevar ContentHash siempre que el flujo que las origina lo tenga disponible.
/// </summary>
public sealed class TenantTermsAcceptance : TenantEntity
{
    private TenantTermsAcceptance() { }

    public Guid AcceptedByUserId { get; private set; }
    public string TermsVersion { get; private set; } = default!;
    public Guid TermsVersionId { get; private set; }
    public string? ContentHash { get; private set; }
    public string AcceptedInContext { get; private set; } = default!;
    public string? AcceptedFromIp { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }

    public static TenantTermsAcceptance Accept(
        Guid tenantId,
        Guid acceptedByUserId,
        string termsVersion,
        Guid termsVersionId,
        string? contentHash,
        string acceptedInContext,
        string? acceptedFromIp,
        string? userAgent,
        DateTime nowUtc
    )
    {
        var acceptance = new TenantTermsAcceptance
        {
            Id = Guid.NewGuid(),
            AcceptedByUserId = acceptedByUserId,
            TermsVersion = termsVersion,
            TermsVersionId = termsVersionId,
            ContentHash = contentHash,
            AcceptedInContext = acceptedInContext,
            AcceptedFromIp = acceptedFromIp,
            UserAgent = userAgent,
            AcceptedAtUtc = nowUtc,
        };
        acceptance.SetTenant(tenantId);
        return acceptance;
    }
}
