using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

internal sealed class FakeTenantEmailProviderRepository : ITenantEmailProviderRepository
{
    private readonly List<TenantEmailProvider> _providers = [];

    public Task AddAsync(TenantEmailProvider provider, CancellationToken ct = default)
    {
        _providers.Add(provider);
        return Task.CompletedTask;
    }

    public Task<Result<TenantEmailProvider>> GetByCodeAsync(
        Guid tenantId,
        string providerCode,
        CancellationToken ct = default
    )
    {
        var provider = _providers.Find(p => p.TenantId == tenantId && p.ProviderCode == providerCode);
        return Task.FromResult(
            provider is null
                ? Result.Failure<TenantEmailProvider>(new Error("TenantEmailProvider.NotFound", "Not found."))
                : Result.Success(provider)
        );
    }

    public Task<Result<TenantEmailProvider>> GetEnabledByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var provider = _providers.Find(p => p.TenantId == tenantId && p.Enabled);
        return Task.FromResult(
            provider is null
                ? Result.Failure<TenantEmailProvider>(
                    new Error("TenantEmailProvider.NotConfigured", "No enabled TenantEmailProvider configured.")
                )
                : Result.Success(provider)
        );
    }
}
