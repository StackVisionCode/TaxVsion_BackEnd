using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPlanRepository
{
    Task<IReadOnlyList<Plan>> GetActiveAsync(CancellationToken ct = default);
    Task<Plan?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Plan?> GetByIdAsync(Guid planId, CancellationToken ct = default);
}

public interface ISubscriptionRepository
{
    Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantSubscription subscription, CancellationToken ct = default);
}
