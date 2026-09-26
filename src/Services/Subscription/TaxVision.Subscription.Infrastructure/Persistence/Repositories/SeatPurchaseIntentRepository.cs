using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SeatPurchaseIntentRepository(SubscriptionDbContext db) : ISeatPurchaseIntentRepository
{
    public async Task AddAsync(SeatPurchaseIntent intent, CancellationToken ct = default) =>
        await db.SeatPurchaseIntents.AddAsync(intent, ct);

    // IgnoreQueryFilters + guardia explícita de tenant (mismo patrón que TenantSubscriptionRepository):
    // el filtro global fail-closed de la DbContext es solo un backstop de defensa y no es fiable como
    // única fuente de tenant en el hot path de lectura — sin esto, el polling GET seats/checkout/{id}
    // devolvía 400 NotFound pese a existir la fila. El TenantId autenticado que pasa el caller acota.
    public Task<SeatPurchaseIntent?> GetByIdAsync(Guid intentId, Guid tenantId, CancellationToken ct = default) =>
        db
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(intent => intent.Id == intentId && intent.TenantId == tenantId, ct);

    // Trackeada (el handler la reutiliza y puede tocarla) y acotada por el tenant autenticado, igual que
    // GetByIdAsync. La más reciente: si hubiera varias abiertas, la última es la que el usuario vio.
    public Task<SeatPurchaseIntent?> GetOpenByTenantAsync(
        Guid tenantId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        db
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .Where(intent =>
                intent.TenantId == tenantId
                && intent.Status == SeatPurchaseIntentStatus.Pending
                && intent.CheckoutUrl != null
                && intent.CheckoutExpiresAtUtc > nowUtc
            )
            .OrderByDescending(intent => intent.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

    // Trackeado y sin filtro de tenant: el consumer del webhook llega por Id de evento y valida el TenantId.
    public Task<SeatPurchaseIntent?> GetByIdForProvisioningAsync(Guid intentId, CancellationToken ct = default) =>
        db.SeatPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(intent => intent.Id == intentId, ct);

    public async Task<IReadOnlyList<SeatPurchaseIntent>> FindStalePendingWithPaymentAsync(
        DateTime olderThanUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .Where(intent =>
                intent.Status == SeatPurchaseIntentStatus.Pending
                && intent.SaaSPaymentId != null
                && intent.CreatedAtUtc < olderThanUtc
            )
            .OrderBy(intent => intent.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
}
