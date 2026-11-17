using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using TaxVision.Subscription.Infrastructure.Growth;
using TaxVision.Subscription.Infrastructure.ServiceAuth;

namespace TaxVision.Subscription.Infrastructure.Reconciliation;

/// <summary>Token M2M para leer el directorio de Tenant: el listado cruza oficinas, se pide como plataforma.</summary>
internal sealed class SubscriptionTenantTimeZoneTokenProvider(ISubscriptionServiceTokenAcquirer tokenAcquirer)
    : ITenantTimeZoneTokenProvider
{
    public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
