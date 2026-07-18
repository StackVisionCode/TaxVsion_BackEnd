using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(SubscriptionDbContext db) : IPlanRepository
{
    public async Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
        await WithVersions(db.Plans.AsNoTracking())
            .Where(plan => plan.Status == PlanStatus.Published)
            .OrderBy(plan => plan.Tier)
            .ToListAsync(ct);

    public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var planCodeResult = PlanCode.Create(code);
        if (planCodeResult.IsFailure)
            return Task.FromResult<SubscriptionPlan?>(null);

        var planCode = planCodeResult.Value;
        return WithVersions(db.Plans.AsNoTracking()).FirstOrDefaultAsync(plan => plan.Code == planCode, ct);
    }

    public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
        WithVersions(db.Plans.AsNoTracking()).FirstOrDefaultAsync(plan => plan.Id == planId, ct);

    private static IQueryable<SubscriptionPlan> WithVersions(IQueryable<SubscriptionPlan> query) =>
        query
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Features)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Entitlements)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.PriceTiers);
}
