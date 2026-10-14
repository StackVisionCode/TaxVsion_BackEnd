using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;

namespace TaxVision.CloudStorage.Infrastructure.Reconciliation;

/// <summary>
/// Seam de token para el job de reconciliación de asignaciones: reutiliza el <see cref="IServiceTokenAcquirer"/>
/// que CloudStorage ya tiene (RateLimit Fase 2) con la identidad de la PlatformTenant — única que acepta
/// el endpoint M2M de reconciliación de Customer.
/// </summary>
internal sealed class CloudStoragePlatformTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
