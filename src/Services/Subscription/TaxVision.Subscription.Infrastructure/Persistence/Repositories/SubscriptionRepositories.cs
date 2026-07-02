using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(SubscriptionDbContext db) : IPlanRepository
{
    public async Task<IReadOnlyList<Plan>> GetActiveAsync(CancellationToken ct = default)
        => await db.Plans
            .AsNoTracking()
            .Where(plan => plan.IsActive)
            .OrderBy(plan => plan.SortOrder)
            .ToListAsync(ct);

    public Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default)
        => db.Plans.AsNoTracking().FirstOrDefaultAsync(plan => plan.Code == code, ct);

    public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken ct = default)
        => db.Plans.AsNoTracking().FirstOrDefaultAsync(plan => plan.Id == planId, ct);
}

public sealed class SubscriptionRepository(SubscriptionDbContext db) : ISubscriptionRepository
{
    public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        => db.Subscriptions.FirstOrDefaultAsync(
            subscription => subscription.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSubscription subscription, CancellationToken ct = default)
        => await db.Subscriptions.AddAsync(subscription, ct);
}
