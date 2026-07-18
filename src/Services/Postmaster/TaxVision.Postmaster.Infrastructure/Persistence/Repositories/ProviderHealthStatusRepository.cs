using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class ProviderHealthStatusRepository(PostmasterDbContext dbContext) : IProviderHealthStatusRepository
{
    public async Task AddAsync(ProviderHealthStatus status, CancellationToken ct = default) =>
        await dbContext.ProviderHealthStatuses.AddAsync(status, ct);

    public async Task<Result<ProviderHealthStatus>> GetAsync(
        ProviderKind providerKind,
        Guid? tenantId,
        string providerCode,
        CancellationToken ct = default
    )
    {
        var status = await dbContext.ProviderHealthStatuses.FirstOrDefaultAsync(
            h => h.ProviderKind == providerKind && h.TenantId == tenantId && h.ProviderCode == providerCode,
            ct
        );
        return status is null
            ? Result.Failure<ProviderHealthStatus>(
                new Error("ProviderHealthStatus.NotFound", $"No health status recorded for provider '{providerCode}'.")
            )
            : Result.Success(status);
    }
}
