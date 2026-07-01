using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class PendingChangeRepository(SubscriptionDbContext db) : IPendingChangeRepository
{
    public Task<PendingSubscriptionChange?> GetByIdWithSubscriptionAsync(Guid id, CancellationToken ct = default) =>
        db.PendingSubscriptionChanges
            .Include(pc => pc.Subscription)
            .FirstOrDefaultAsync(pc => pc.Id == id, ct);

    public async Task AddAsync(PendingSubscriptionChange change, CancellationToken ct = default) =>
        await db.PendingSubscriptionChanges.AddAsync(change, ct);
}
