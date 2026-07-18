using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers.Queries.GetProviderStatus;

public static class GetProviderStatusHandler
{
    public static async Task<ProviderStatusDto> Handle(
        GetProviderStatusQuery query,
        ISystemEmailProviderRepository systemProviders,
        ITenantEmailProviderRepository tenantProviders,
        IProviderHealthStatusRepository healthStatuses,
        CancellationToken ct
    )
    {
        var systemLookup = await systemProviders.GetEnabledDefaultAsync(ct);
        var tenantLookup = await tenantProviders.GetEnabledByTenantAsync(query.TenantId, ct);

        if (tenantLookup.IsFailure)
            return new ProviderStatusDto(systemLookup.IsSuccess, false, false, null, null);

        var provider = tenantLookup.Value;
        var healthLookup = await healthStatuses.GetAsync(
            ProviderKind.Tenant,
            query.TenantId,
            provider.ProviderCode,
            ct
        );
        var healthy = healthLookup.IsFailure || healthLookup.Value.CircuitBreakerState != CircuitBreakerState.Open;
        var lastCheckAtUtc = healthLookup.IsSuccess ? healthLookup.Value.LastCheckAtUtc : (DateTime?)null;

        return new ProviderStatusDto(
            systemLookup.IsSuccess,
            true,
            healthy,
            lastCheckAtUtc,
            new TenantProviderConfigSummary(provider.FromAddressDefault, provider.Host)
        );
    }
}
