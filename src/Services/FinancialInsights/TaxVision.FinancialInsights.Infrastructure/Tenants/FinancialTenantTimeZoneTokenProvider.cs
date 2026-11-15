using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;

namespace TaxVision.FinancialInsights.Infrastructure.Tenants;

/// <summary>Token M2M para leer el directorio de Tenant: el listado cruza oficinas, se pide como plataforma.</summary>
internal sealed class FinancialTenantTimeZoneTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : ITenantTimeZoneTokenProvider
{
    public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
