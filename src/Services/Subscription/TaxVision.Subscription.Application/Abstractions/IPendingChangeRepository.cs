using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPendingChangeRepository
{
    Task<PendingSubscriptionChange?> GetByIdWithSubscriptionAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PendingSubscriptionChange change, CancellationToken ct = default);
}
