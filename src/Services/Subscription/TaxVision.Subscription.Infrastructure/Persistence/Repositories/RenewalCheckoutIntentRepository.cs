using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class RenewalCheckoutIntentRepository(SubscriptionDbContext db) : IRenewalCheckoutIntentRepository
{
    public async Task AddAsync(SubscriptionRenewalIntent intent, CancellationToken ct = default) =>
        await db.SubscriptionRenewalIntents.AddAsync(intent, ct);

    // IgnoreQueryFilters + guardia explícita de tenant (mismo patrón que SeatPurchaseIntentRepository): el
    // filtro global fail-closed es solo un backstop y no es fiable como única fuente de tenant en el hot path.
    public Task<SubscriptionRenewalIntent?> GetByIdAsync(
        Guid intentId,
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        db
            .SubscriptionRenewalIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(intent => intent.Id == intentId && intent.TenantId == tenantId, ct);

    // Trackeado y sin filtro de tenant: el consumer del webhook llega por Id de evento y valida el TenantId.
    public Task<SubscriptionRenewalIntent?> GetByIdForProvisioningAsync(
        Guid intentId,
        CancellationToken ct = default
    ) => db.SubscriptionRenewalIntents.IgnoreQueryFilters().FirstOrDefaultAsync(intent => intent.Id == intentId, ct);

    public async Task<IReadOnlyList<SubscriptionRenewalIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .SubscriptionRenewalIntents.IgnoreQueryFilters()
            .Where(intent =>
                intent.Status == SubscriptionRenewalIntentStatus.Pending
                && intent.SaaSPaymentId != null
                && intent.CreatedAtUtc < olderThanUtc
            )
            .OrderBy(intent => intent.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
}
