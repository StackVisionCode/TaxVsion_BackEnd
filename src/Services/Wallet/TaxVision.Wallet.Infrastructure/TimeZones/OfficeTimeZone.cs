using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Settings.Abstractions;

namespace TaxVision.Wallet.Infrastructure.TimeZones;

/// <summary>Lee la zona de la proyección compartida; sin proyección, el resolver ya devuelve UTC marcado.</summary>
internal sealed class OfficeTimeZone(ITenantTimeZoneResolver resolver) : IOfficeTimeZone
{
    public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
        resolver.ResolveAsync(tenantId, ct);
}

/// <summary>Token M2M para leer el directorio de Tenant: el listado cruza oficinas, se pide como plataforma.</summary>
internal sealed class WalletTenantTimeZoneTokenProvider(IServiceTokenAcquirer tokenAcquirer)
    : ITenantTimeZoneTokenProvider
{
    public Task<string?> GetServiceTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
