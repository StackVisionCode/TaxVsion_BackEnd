using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Barre intenciones de checkout vencidas (CheckoutExpiresAtUtc &lt; now, estado Pending/AwaitingPayment) y las
/// deja en Expired, publicando el evento correspondiente. Red de seguridad para cuando el usuario cerró la
/// pasarela sin pulsar cancelar: sin esto, la intención se queda "Finish payment" para siempre y el guard de
/// doble cobro bloquea nuevos intentos hasta que algo la mueva.
/// </summary>
public sealed class CheckoutIntentExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<CheckoutIntentExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(100))
{
    private const int BatchSize = 100;

    protected override string JobName => "checkout-intent-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var correlationId = Guid.NewGuid().ToString("N");
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var jobLogger = services.GetRequiredService<ILogger<CheckoutIntentExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var batch = new CheckoutExpirationBatch(
            await db
                .SeatPurchaseIntents.IgnoreQueryFilters()
                .Where(i =>
                    i.Status == SeatPurchaseIntentStatus.Pending
                    && i.CheckoutExpiresAtUtc != null
                    && i.CheckoutExpiresAtUtc < nowUtc
                )
                .OrderBy(i => i.CheckoutExpiresAtUtc)
                .Take(BatchSize)
                .Select(i => i.Id)
                .ToListAsync(ct),
            await db
                .AddOnPurchaseIntents.IgnoreQueryFilters()
                .Where(i =>
                    i.Status == AddOnPurchaseIntentStatus.Pending
                    && i.CheckoutExpiresAtUtc != null
                    && i.CheckoutExpiresAtUtc < nowUtc
                )
                .OrderBy(i => i.CheckoutExpiresAtUtc)
                .Take(BatchSize)
                .Select(i => i.Id)
                .ToListAsync(ct),
            await db
                .SubscriptionRenewalIntents.IgnoreQueryFilters()
                .Where(i =>
                    i.Status == SubscriptionRenewalIntentStatus.Pending
                    && i.CheckoutExpiresAtUtc != null
                    && i.CheckoutExpiresAtUtc < nowUtc
                )
                .OrderBy(i => i.CheckoutExpiresAtUtc)
                .Take(BatchSize)
                .Select(i => i.Id)
                .ToListAsync(ct),
            await db
                .Subscriptions.IgnoreQueryFilters()
                .SelectMany(s => s.PlanChangeRequests)
                .Where(r =>
                    r.Status == PlanChangeRequestStatus.AwaitingPayment
                    && r.CheckoutExpiresAtUtc != null
                    && r.CheckoutExpiresAtUtc < nowUtc
                )
                .OrderBy(r => r.CheckoutExpiresAtUtc)
                .Take(BatchSize)
                .Select(r => r.Id)
                .ToListAsync(ct)
        );

        var total = await ExpireAsync(batch, correlationId, nowUtc, ct);

        if (total > 50)
            jobLogger.LogWarning(
                "CheckoutIntentExpirationJob expired {Count} intents in a single tick — unusually high, check provider connectivity.",
                total
            );
        else if (total > 0)
            jobLogger.LogInformation("CheckoutIntentExpirationJob expired {Count} intent(s).", total);
    }

    internal sealed record CheckoutExpirationBatch(
        IReadOnlyList<Guid> SeatIntentIds,
        IReadOnlyList<Guid> AddOnIntentIds,
        IReadOnlyList<Guid> RenewalIntentIds,
        IReadOnlyList<Guid> UpgradeRequestIds
    );

    /// <summary>Lo que hace falta para cancelar la sesión en el proveedor antes de expirar.</summary>
    private sealed record PendingCheckout(Guid TenantId, Guid? SaaSPaymentId);

    internal async Task<int> ExpireAsync(
        CheckoutExpirationBatch batch,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var total = 0;
        total += await ExpireEachAsync(
            batch.SeatIntentIds,
            "Seat intent",
            async (db, id) =>
                await db.SeatPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct)
                    is { Status: SeatPurchaseIntentStatus.Pending } intent
                    ? new PendingCheckout(intent.TenantId, intent.SaaSPaymentId)
                    : null,
            (unit, id) => ExpireSeatAsync(unit, id, correlationId, nowUtc, ct),
            ct
        );
        total += await ExpireEachAsync(
            batch.AddOnIntentIds,
            "Add-on intent",
            async (db, id) =>
                await db.AddOnPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct)
                    is { Status: AddOnPurchaseIntentStatus.Pending } intent
                    ? new PendingCheckout(intent.TenantId, intent.SaaSPaymentId)
                    : null,
            (unit, id) => ExpireAddOnAsync(unit, id, correlationId, nowUtc, ct),
            ct
        );
        total += await ExpireEachAsync(
            batch.RenewalIntentIds,
            "Renewal intent",
            async (db, id) =>
                await db.SubscriptionRenewalIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct)
                    is { Status: SubscriptionRenewalIntentStatus.Pending } intent
                    ? new PendingCheckout(intent.TenantId, intent.SaaSPaymentId)
                    : null,
            (unit, id) => ExpireRenewalAsync(unit, id, correlationId, nowUtc, ct),
            ct
        );
        total += await ExpireEachAsync(
            batch.UpgradeRequestIds,
            "Upgrade request",
            async (db, id) =>
                await db
                    .Subscriptions.IgnoreQueryFilters()
                    .SelectMany(s => s.PlanChangeRequests)
                    .FirstOrDefaultAsync(r => r.Id == id, ct)
                    is { Status: PlanChangeRequestStatus.AwaitingPayment } request
                    ? new PendingCheckout(request.TenantId, request.SaaSPaymentId)
                    : null,
            (unit, id) => ExpireUpgradeAsync(unit, id, correlationId, nowUtc, ct),
            ct
        );
        return total;
    }

    // Por ítem: la sesión se cancela en el proveedor fuera de la transacción (cancelar es idempotente); después,
    // una unidad (BB-3) expira la intención y publica su evento juntos. Un fallo no arrastra a los demás.
    private async Task<int> ExpireEachAsync(
        IReadOnlyList<Guid> ids,
        string kind,
        Func<SubscriptionDbContext, Guid, Task<PendingCheckout?>> readPending,
        Func<JobUnitContext<SubscriptionDbContext>, Guid, Task<bool>> expireOne,
        CancellationToken ct
    )
    {
        var expired = 0;
        foreach (var id in ids)
        {
            try
            {
                await using var scope = ScopeFactory.CreateAsyncScope();
                var pending = await readPending(scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>(), id);
                if (pending is null)
                    continue;

                if (pending.SaaSPaymentId is { } paymentId)
                    await scope
                        .ServiceProvider.GetRequiredService<IHostedCheckoutCancellationClient>()
                        .CancelAsync(pending.TenantId, paymentId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "{Kind} expiration failed for {Id}.", kind, id);
                continue;
            }

            var result = await RunUnitAsync((unit, _) => expireOne(unit, id), ct);
            if (result.Outcome == JobUnitOutcome.Committed)
                expired++;
            else if (result.Outcome == JobUnitOutcome.Conflict)
                Logger.LogInformation("{Kind} {Id} changed by another writer; not expired.", kind, id);
            else if (result.Outcome == JobUnitOutcome.Failed)
                Logger.LogWarning("{Kind} expiration failed for {Id}.", kind, id);
        }
        return expired;
    }

    private static async Task<bool> ExpireSeatAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var intent = await unit
            .DbContext.SeatPurchaseIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != SeatPurchaseIntentStatus.Pending)
            return false;

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != SeatPurchaseIntentStatus.Expired)
            return false;

        await unit.PublishAsync(
            new SeatCheckoutExpiredIntegrationEvent
            {
                TenantId = intent.TenantId,
                SeatPurchaseIntentId = intent.Id,
                SaaSPaymentId = intent.SaaSPaymentId,
                CorrelationId = correlationId,
            }
        );
        return true;
    }

    private static async Task<bool> ExpireAddOnAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var intent = await unit
            .DbContext.AddOnPurchaseIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != AddOnPurchaseIntentStatus.Pending)
            return false;

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != AddOnPurchaseIntentStatus.Expired)
            return false;

        await unit.PublishAsync(
            new AddOnCheckoutExpiredIntegrationEvent
            {
                TenantId = intent.TenantId,
                AddOnPurchaseIntentId = intent.Id,
                SaaSPaymentId = intent.SaaSPaymentId,
                CorrelationId = correlationId,
            }
        );
        return true;
    }

    private static async Task<bool> ExpireRenewalAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var intent = await unit
            .DbContext.SubscriptionRenewalIntents.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != SubscriptionRenewalIntentStatus.Pending)
            return false;

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != SubscriptionRenewalIntentStatus.Expired)
            return false;

        await unit.PublishAsync(
            new SubscriptionRenewalCheckoutExpiredIntegrationEvent
            {
                TenantId = intent.TenantId,
                RenewalIntentId = intent.Id,
                SaaSPaymentId = intent.SaaSPaymentId,
                CorrelationId = correlationId,
            }
        );
        return true;
    }

    private static async Task<bool> ExpireUpgradeAsync(
        JobUnitContext<SubscriptionDbContext> unit,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var request = await unit
            .DbContext.Subscriptions.IgnoreQueryFilters()
            .SelectMany(s => s.PlanChangeRequests)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || request.Status != PlanChangeRequestStatus.AwaitingPayment)
            return false;

        var result = request.Expire(nowUtc);
        if (result.IsFailure || request.Status != PlanChangeRequestStatus.Expired)
            return false;

        await unit.PublishAsync(
            new PlanChangeUpgradeExpiredIntegrationEvent
            {
                TenantId = request.TenantId,
                PlanChangeRequestId = request.Id,
                SaaSPaymentId = request.SaaSPaymentId,
                CorrelationId = correlationId,
            }
        );
        return true;
    }
}
