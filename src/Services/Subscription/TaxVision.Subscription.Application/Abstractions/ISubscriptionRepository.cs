using Sub = TaxVision.Subscription.Domain.Subscriptions.Subscription;

namespace TaxVision.Subscription.Application.Abstractions;

public interface ISubscriptionRepository
{
    Task<Sub?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Sub?> GetActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<bool> ExistsForTenantAsync(Guid tenantId, CancellationToken ct