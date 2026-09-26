using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class AddOnPurchaseIntentRepository(SubscriptionDbContext db) : IAddOnPurchaseIntentRepository
{
    public async Task AddAsync(AddOnPurchaseIntent intent, CancellationToken ct = default) =>
        await db.AddOnPurchaseIntents.AddAsync(intent, ct);

    // IgnoreQueryFilters + guardia explícita de tenant, igual que SeatPurchaseIntentRepository: el filtro
    // global fail-closed es un backstop, no la fuente de tenant del hot path de lectura.
    public Task<AddOnPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default) =>
        db
            .AddOnPurchaseIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(intent => intent.Id == intentId && intent.TenantId == tenantId, ct);

    // La más reciente: si hubiera varias abiertas del mismo add-on, la última es la que el usuario vio.
    public Task<AddOnPurchaseIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        string addOnCode,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        db
            .AddOnPurchaseIntents.IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == tenantId
                && intent.AddOnCode == addOnCode
                && intent.Status == AddOnPurchaseIntentStatus.Pending
                && intent.CheckoutUrl != null
                && intent.CheckoutExpiresAtUtc > nowUtc
            )
            .OrderByDescending(intent => intent.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    // Trackeado y sin filtro de tenant: el consumer del webhook llega por Id de evento y valida el TenantId.
    public Task<AddOnPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default) =>
        db.AddOnPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(intent => intent.Id == intentId, ct);

    public async Task<IReadOnlyList<AddOnPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .AddOnPurchaseIntents.IgnoreQueryFilters()
            .Where(intent =>
                intent.Status == AddOnPurchaseIntentStatus.Pending
                && intent.SaaSPaymentId != null
                && intent.CreatedAtUtc < olderThanUtc
            )
            .OrderBy(intent => intent.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
}
