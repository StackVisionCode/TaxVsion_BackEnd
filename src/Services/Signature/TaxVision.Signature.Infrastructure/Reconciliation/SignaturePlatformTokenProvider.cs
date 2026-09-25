using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;

namespace TaxVision.Signature.Infrastructure.Reconciliation;

// Seam de token del kit de visibilidad para Signature: usa el acquirer M2M compartido con la identidad de
// la PlatformTenant (única que acepta el endpoint de reconciliación de asignaciones de Customer).
internal sealed class SignaturePlatformTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
