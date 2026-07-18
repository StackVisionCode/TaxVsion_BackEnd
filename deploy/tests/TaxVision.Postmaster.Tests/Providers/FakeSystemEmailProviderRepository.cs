using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

internal sealed class FakeSystemEmailProviderRepository : ISystemEmailProviderRepository
{
    private readonly List<SystemEmailProvider> _providers = [];

    public Task AddAsync(SystemEmailProvider provider, CancellationToken ct = default)
    {
        _providers.Add(provider);
        return Task.CompletedTask;
    }

    public Task<Result<SystemEmailProvider>> GetByCodeAsync(string providerCode, CancellationToken ct = default)
    {
        var provider = _providers.Find(p => p.ProviderCode == providerCode);
        return Task.FromResult(
            provider is null
                ? Result.Failure<SystemEmailProvider>(
                    new Error("SystemEmailProvider.NotFound", $"Provider '{providerCode}' not found.")
                )
                : Result.Success(provider)
        );
    }

    public Task<Result<SystemEmailProvider>> GetEnabledDefaultAsync(CancellationToken ct = default)
    {
        var provider = _providers.Find(p => p.Enabled);
        return Task.FromResult(
            provider is null
                ? Result.Failure<SystemEmailProvider>(
                    new Error("SystemEmailProvider.NoneEnabled", "No enabled SystemEmailProvider is configured.")
                )
                : Result.Success(provider)
        );
    }
}
