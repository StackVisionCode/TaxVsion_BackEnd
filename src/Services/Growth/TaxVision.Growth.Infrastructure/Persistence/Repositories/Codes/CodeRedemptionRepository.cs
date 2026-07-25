using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Redemptions;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeRedemptionRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : ICodeRedemptionRepository
{
    public Task<CodeRedemption?> GetByIdAsync(Guid tenantId, Guid redemptionId, CancellationToken ct = default) =>
        FindAsync(tenantId, redemption => redemption.Id == redemptionId, redemptionId, ct);

    public Task<CodeRedemption?> GetByReservationIdAsync(
        Guid tenantId,
        Guid reservationId,
        CancellationToken ct = default
    ) => FindAsync(tenantId, redemption => redemption.ReservationId == reservationId, reservationId, ct);

    public async Task AddAsync(CodeRedemption redemption, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, redemption.TenantId);
        await dbContext.CodeRedemptions.AddAsync(redemption, ct);
    }

    private Task<CodeRedemption?> FindAsync(
        Guid tenantId,
        System.Linq.Expressions.Expression<Func<CodeRedemption, bool>> predicate,
        Guid requiredId,
        CancellationToken ct
    ) =>
        !TenantRepositoryGuard.Matches(tenantContext, tenantId) || requiredId == Guid.Empty
            ? Task.FromResult<CodeRedemption?>(null)
            : dbContext
                .CodeRedemptions.IgnoreQueryFilters()
                .Where(redemption => redemption.TenantId == tenantId)
                .FirstOrDefaultAsync(predicate, ct);
}
