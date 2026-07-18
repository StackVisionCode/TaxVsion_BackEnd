using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class SystemEmailProviderRepository(PostmasterDbContext dbContext) : ISystemEmailProviderRepository
{
    public async Task AddAsync(SystemEmailProvider provider, CancellationToken ct = default) =>
        await dbContext.SystemEmailProviders.AddAsync(provider, ct);

    public async Task<Result<SystemEmailProvider>> GetByCodeAsync(string providerCode, CancellationToken ct = default)
    {
        var provider = await dbContext.SystemEmailProviders.FirstOrDefaultAsync(
            p => p.ProviderCode == providerCode,
            ct
        );
        return provider is null
            ? Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.NotFound", $"Provider '{providerCode}' not found.")
            )
            : Result.Success(provider);
    }

    public async Task<Result<SystemEmailProvider>> GetEnabledDefaultAsync(CancellationToken ct = default)
    {
        var provider = await dbContext
            .SystemEmailProviders.Where(p => p.Enabled)
            .OrderBy(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return provider is null
            ? Result.Failure<SystemEmailProvider>(
                new Error("SystemEmailProvider.NoneEnabled", "No enabled SystemEmailProvider is configured.")
            )
            : Result.Success(provider);
    }
}
