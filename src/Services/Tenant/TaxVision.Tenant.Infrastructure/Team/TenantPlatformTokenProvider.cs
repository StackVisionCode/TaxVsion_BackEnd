using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Infrastructure.Branding;

namespace TaxVision.Tenant.Infrastructure.Team;

// Token M2M de la PlatformTenant (la única que acepta la reconciliación de asignaciones de Customer), con el
// mismo acquirer de Tenant que ya usan CloudStorage y Billing.
internal sealed class TenantPlatformTokenProvider(ITenantServiceTokenAcquirer tokenAcquirer)
    : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
