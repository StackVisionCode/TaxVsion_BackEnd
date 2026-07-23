using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class TenantEmailProviderRepository(PostmasterDbContext dbContext) : ITenantEmailProviderRepository
{
    public async Task AddAsync(TenantEmailProvider provider, CancellationToken ct = default) =>
        await dbContext.TenantEmailProviders.AddAsync(provider, ct);

    public async Task<Result<TenantEmailProvider>> GetByCodeAsync(
        Guid tenantId,
        string providerCode,
        CancellationToken ct = default
    )
    {
        var provider = await dbContext
            .TenantEmailProviders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ProviderCode == providerCode, ct);
        return provider is null
            ? Result.Failure<TenantEmailProvider>(
                new Error("TenantEmailProvider.NotFound", $"Provider '{providerCode}' not found for tenant {tenantId}.")
            )
            : Result.Success(provider);
    }

    public async Task<Result<TenantEmailProvider>> GetEnabledByTenantAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        var provider = await dbContext
            .TenantEmailProviders.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.Enabled)
            .OrderBy(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return provider is null
            ? Result.Failure<TenantEmailProvider>(
                new Error(
                    "TenantEmailProvider.NotConfigured",
                    $"No enabled TenantEmailProvider configured for tenant {tenantId}."
                )
            )
            : Result.Success(provider);
    }
}
