using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Guarda intenciones de renovación en memoria y resuelve por Id. Espejo de
/// <see cref="FakeSeatPurchaseIntentRepository"/>.</summary>
public sealed class FakeRenewalCheckoutIntentRepository : IRenewalCheckoutIntentRepository
{
    public List<SubscriptionRenewalIntent> Added { get; } = [];

    public FakeRenewalCheckoutIntentRepository(params SubscriptionRenewalIntent[] seed) => Added.AddRange(seed);

    public Task AddAsync(SubscriptionRenewalIntent intent, CancellationToken ct = default)
    {
        Added.Add(intent);
        return Task.CompletedTask;
    }

    public Task<SubscriptionRenewalIntent?> GetByIdAsync(
        Guid intentId,
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId && intent.TenantId == tenantId));

    public Task<SubscriptionRenewalIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Added
                .Where(intent => intent.TenantId == tenantId && intent.IsOpen(nowUtc))
                .OrderByDescending(intent => intent.CreatedAtUtc)
                .FirstOrDefault()
        );

    public Task<SubscriptionRenewalIntent?> GetByIdForProvisioningAsync(
        Guid intentId,
        CancellationToken ct = default
    ) => Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId));

    public Task<IReadOnlyList<SubscriptionRenewalIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SubscriptionRenewalIntent>>(
            Added
                .Where(intent =>
                    intent.Status == SubscriptionRenewalIntentStatus.Pending
                    && intent.SaaSPaymentId != null
                    && intent.CreatedAtUtc < olderThanUtc
                )
                .OrderBy(intent => intent.CreatedAtUtc)
                .Take(batchSize)
                .ToList()
        );
}
