using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class LateCommitReviewRepository(GrowthDbContext dbContext, ITenantContext tenantContext)
    : ILateCommitReviewRepository
{
    public async Task<bool> RecordAsync(LateCommitReview review, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, review.TenantId);

        var exists = await dbContext
            .LateCommitReviews.IgnoreQueryFilters()
            .AnyAsync(
                r => r.CodeReservationId == review.CodeReservationId && r.SourceEventId == review.SourceEventId,
                ct
            );
        if (exists)
            return false;

        var entry = await dbContext.LateCommitReviews.AddAsync(review, ct);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (ConflictException)
        {
            // Otra entrega del mismo evento la registró primero (índice único).
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<LateCommitReview>> ListPendingAsync(int take, CancellationToken ct = default) =>
        await dbContext
            .LateCommitReviews.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.ResolvedAtUtc == null)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);
}
