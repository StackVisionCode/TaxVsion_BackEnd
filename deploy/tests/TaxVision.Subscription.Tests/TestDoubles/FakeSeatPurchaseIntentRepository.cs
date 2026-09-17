using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Guarda intenciones en memoria y resuelve por Id (tenant-scoped o para aprovisionamiento).</summary>
public sealed class FakeSeatPurchaseIntentRepository : ISeatPurchaseIntentRepository
{
    public List<SeatPurchaseIntent> Added { get; } = [];

    public FakeSeatPurchaseIntentRepository(params SeatPurchaseIntent[] seed) => Added.AddRange(seed);

    public Task AddAsync(SeatPurchaseIntent intent, CancellationToken ct = default)
    {
        Added.Add(intent);
        return Task.CompletedTask;
    }

    public Task<SeatPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId && intent.TenantId == tenantId));

    public Task<SeatPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default) =>
        Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId));

    public Task<IReadOnlyList<SeatPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<SeatPurchaseIntent>>(
            Added
                .Where(intent =>
                    intent.Status == SeatPurchaseIntentStatus.Pending
                    && intent.SaaSPaymentId != null
                    && intent.CreatedAtUtc < olderThanUtc
                )
                .OrderBy(intent => intent.CreatedAtUtc)
                .Take(batchSize)
                .ToList()
        );
}
