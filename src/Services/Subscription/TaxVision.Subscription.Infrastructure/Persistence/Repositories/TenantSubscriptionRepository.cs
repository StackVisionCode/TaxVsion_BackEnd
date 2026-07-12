using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class TenantSubscriptionRepository(SubscriptionDbContext db) : ISubscriptionRepository
{
    public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Subscriptions.FirstOrDefaultAsync(subscription => subscription.TenantId == tenantId, ct);

    public async Task AddAsync(TenantSubscription subscription, CancellationToken ct = default) =>
        await db.Subscriptions.AddAsync(subscription, ct);
}
