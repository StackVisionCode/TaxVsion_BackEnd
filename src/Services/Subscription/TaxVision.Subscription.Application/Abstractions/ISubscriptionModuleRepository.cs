using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

public interface ISubscriptionModuleRepository
{
    Task AddAsync(SubscriptionModule module, CancellationToken ct = default);
    Task<SubscriptionModule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SubscriptionModule?> GetBySubsc