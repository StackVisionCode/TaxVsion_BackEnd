using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;

namespace TaxVision.Correspondence.Infrastructure.Reconciliation;

// Seam de token del kit de visibilidad: el acquirer M2M del servicio con la identidad de la PlatformTenant,
// la única que acepta la reconciliación de asignaciones de Customer.
internal sealed class CorrespondencePlatformTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
