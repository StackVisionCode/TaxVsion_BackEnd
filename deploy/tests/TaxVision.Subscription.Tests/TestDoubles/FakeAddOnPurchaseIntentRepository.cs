using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Guarda intenciones de add-on en memoria y resuelve por Id (tenant-scoped o para aprovisionar).</summary>
public sealed class FakeAddOnPurchaseIntentRepository : IAddOnPurchaseIntentRepository
{
    public List<AddOnPurchaseIntent> Added { get; } = [];

    public FakeAddOnPurchaseIntentRepository(params AddOnPurchaseIntent[] seed) => Added.AddRange(seed);

    public Task AddAsync(AddOnPurchaseIntent intent, CancellationToken ct = default)
    {
        Added.Add(intent);
        return Task.CompletedTask;
    }

    public Task<AddOnPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId && intent.TenantId == tenantId));

    public Task<AddOnPurchaseIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        string addOnCode,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Added
                .Where(intent =>
                    intent.TenantId == tenantId
                    && string.Equals(intent.AddOnCode, addOnCode, StringComparison.OrdinalIgnoreCase)
                    && intent.IsOpen(nowUtc)
                )
                .OrderByDescending(intent => intent.CreatedAtUtc)
                .FirstOrDefault()
        );

    public Task<AddOnPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default) =>
        Task.FromResult(Added.FirstOrDefault(intent => intent.Id == intentId));

    public Task<IReadOnlyList<AddOnPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<AddOnPurchaseIntent>>(
            Added
                .Where(intent =>
                    intent.Status == AddOnPurchaseIntentStatus.Pending
                    && intent.SaaSPaymentId != null
                    && intent.CreatedAtUtc < olderThanUtc
                )
                .OrderBy(intent => intent.CreatedAtUtc)
                .Take(batchSize)
                .ToList()
        );
}
