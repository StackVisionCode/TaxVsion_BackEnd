using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PlanRateLimitRepository(SubscriptionDbContext db) : IPlanRateLimitRepository
{
    public async Task<IReadOnlyList<PlanRateLimit>> GetAllAsync(CancellationToken ct = default) =>
        await db.PlanRateLimits.AsNoTracking().ToListAsync(ct);
}
