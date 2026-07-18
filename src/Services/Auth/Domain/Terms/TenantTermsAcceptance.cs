using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Terms;

/// <summary>
/// Registro inmutable (append-only) de que un usuario acepto, en nombre del tenant,
/// una version puntual del ToS/AUP. La version vigente exigida vive en TermsOptions;
/// este historial existe para poder probar, ante una disputa legal, que el tenant
/// acepto esa version especifica en un momento dado — nunca se actualiza in place.
/// </summary>
public sealed class TenantTermsAcceptance : TenantEntity
{
    private TenantTermsAcceptance() { }

    public Guid AcceptedByUserId { get; private set; }
    public string TermsVersion { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }

    public static TenantTermsAcceptance Accept(
        Guid tenantId,
        Guid acceptedByUserId,
        string termsVersion,
        string? ipAddress,
        string? userAgent,
        DateTime nowUtc
    )
    {
        var acceptance = new TenantTermsAcceptance
        {
            Id = Guid.NewGuid(),
            AcceptedByUserId = acceptedByUserId,
            TermsVersion = termsVersion,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AcceptedAtUtc = nowUtc,
        };
        acceptance.SetTenant(tenantId);
        return acceptance;
    }
}
