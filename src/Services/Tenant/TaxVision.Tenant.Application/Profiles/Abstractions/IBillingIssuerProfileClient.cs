using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Profiles.Abstractions;

/// <summary>
/// Lectura M2M de los emisores de factura que las oficinas cargaron en Billing antes de que el perfil
/// viviera en Tenant (<c>GET internal/billing/issuer-profiles?page=&amp;size=</c>). Solo la usa la
/// importación única.
/// </summary>
public interface IBillingIssuerProfileClient
{
    /// <summary>Una página. Falla con <see cref="BillingIssuerProfileErrors.Unavailable"/> si Billing no contesta bien.</summary>
    Task<Result<BillingIssuerProfilePage>> GetPageAsync(int page, int size, CancellationToken ct = default);
}
