using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Compensations;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeCompensationRepository(
    GrowthDbContext dbContext,
    ITenantContext tenantContext
) : ICodeCompensationRepository
{
    public Task<CodeCompensation?> GetBySourceEventIdAsync(
        Guid tenantId,
        Guid redemptionId,
        Guid sourceEventId,
        CancellationToken ct = default
    ) =>
        !TenantRepositoryGuard.Matches(tenantContext, tenantId)
        || redemptionId == Guid.Empty
        || sourceEventId == Guid.Empty
            ? Task.FromResult<CodeCompensation?>(null)
            : dbContext.CodeCompensations.FirstOrDefaultAsync(
                compensation =>
                    compensation.TenantId == tenantId
                    && compensation.RedemptionId == redemptionId
                    && compensation.SourceEventId == sourceEventId,
                ct
            );

    public async Task<long> GetCumulativeAdjustmentAmountCentsAsync(
        Guid tenantId,
        Guid redemptionId,
        CancellationToken ct = default
    )
    {
        if (
            !TenantRepositoryGuard.Matches(tenantContext, tenantId)
            || redemptionId == Guid.Empty
        )
            return 0;

        return await dbContext
                .CodeCompensations.Where(compensation =>
                    compensation.TenantId == tenantId
                    && compensation.RedemptionId == redemptionId
                )
                .MaxAsync(
                    compensation => (long?)compensation.CumulativeAdjustmentAmountCents,
                    ct
                )
            ?? 0;
    }

    public async Task AddAsync(CodeCompensation compensation, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, compensation.TenantId);
        await dbContext.CodeCompensations.AddAsync(compensation, ct);
    }
}
