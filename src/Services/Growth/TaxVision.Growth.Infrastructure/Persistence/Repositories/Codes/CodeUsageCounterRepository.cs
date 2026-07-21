using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Usage;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeUsageCounterRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : ICodeUsageCounterRepository
{
    public async Task<Result<CodeUsageCounter>> GetOrCreateForUpdateAsync(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey,
        long maxRedemptions,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, tenantId))
        {
            return Result.Failure<CodeUsageCounter>(
                new Error(
                    "Codes.CodeUsageCounter.TenantMismatch",
                    "The requested usage counter is outside the active tenant scope."
                )
            );
        }

        var existing = await FindAsync(tenantId, codeDefinitionId, dimension, scopeKey, ct);
        if (existing is not null)
            return Result.Success(existing);

        var created = CodeUsageCounter.Create(tenantId, codeDefinitionId, dimension, scopeKey, maxRedemptions, nowUtc);
        if (created.IsFailure)
            return created;

        await dbContext.CodeUsageCounters.AddAsync(created.Value, ct);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return created;
        }
        catch (ConflictException)
        {
            dbContext.Entry(created.Value).State = EntityState.Detached;
            var winner = await FindAsync(tenantId, codeDefinitionId, dimension, scopeKey, ct);
            return winner is not null
                ? Result.Success(winner)
                : Result.Failure<CodeUsageCounter>(
                    new Error(
                        "Codes.CodeUsageCounter.ConcurrentCreationFailed",
                        "The concurrent usage counter could not be reloaded."
                    )
                );
        }
    }

    private Task<CodeUsageCounter?> FindAsync(
        Guid tenantId,
        Guid codeDefinitionId,
        CodeUsageDimension dimension,
        CodeUsageScopeKey scopeKey,
        CancellationToken ct
    ) =>
        dbContext.CodeUsageCounters.FirstOrDefaultAsync(
            counter =>
                counter.TenantId == tenantId
                && counter.CodeDefinitionId == codeDefinitionId
                && counter.Dimension == dimension
                && counter.ScopeKey == scopeKey,
            ct
        );
}
