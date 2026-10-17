using BuildingBlocks.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PlanRateLimitRepository(SubscriptionDbContext db) : IPlanRateLimitRepository
{
    public async Task<IReadOnlyList<PlanRateLimit>> GetAllAsync(CancellationToken ct = default) =>
        await db.PlanRateLimits.AsNoTracking().ToListAsync(ct);

    public async Task<IReadOnlyList<PlanRateLimit>> GetByPlanCodeAsync(string planCode, CancellationToken ct = default)
    {
        var codeResult = PlanCode.Create(planCode);
        if (codeResult.IsFailure)
            return [];

        var code = codeResult.Value;
        return await db
            .PlanRateLimits.AsNoTracking()
            .Where(rl => rl.PlanCode == code)
            .OrderBy(rl => rl.Category)
            .ToListAsync(ct);
    }

    public async Task<PlanRateLimit?> GetForUpdateAsync(
        string planCode,
        RateLimitCategory category,
        CancellationToken ct = default
    )
    {
        var codeResult = PlanCode.Create(planCode);
        if (codeResult.IsFailure)
            return null;

        var code = codeResult.Value;
        return await db.PlanRateLimits.FirstOrDefaultAsync(rl => rl.PlanCode == code && rl.Category == category, ct);
    }

    public async Task AddAsync(PlanRateLimit rateLimit, CancellationToken ct = default)
    {
        await db.PlanRateLimits.AddAsync(rateLimit, ct);
    }
}
