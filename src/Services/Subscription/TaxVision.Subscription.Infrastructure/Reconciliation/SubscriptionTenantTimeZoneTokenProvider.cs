using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using TaxVision.Subscription.Infrastructure.Growth;

namespace TaxVision.Subscription.Infrastructure.Reconciliation;

/// <summary>Token M2M para leer el directorio de Tenant: el listado cruza oficinas, se pide como plataforma.</summary>
internal sealed class SubscriptionTenantTimeZoneTokenProvider(IGrowthServiceTokenAcquirer tokenAcquirer)
    : ITenantTimeZoneTokenProvider
{
    public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
