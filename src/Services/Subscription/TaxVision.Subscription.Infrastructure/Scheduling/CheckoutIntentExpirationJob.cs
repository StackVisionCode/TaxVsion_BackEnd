using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence;
using Wolverine;

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
        var correlation = services.GetRequiredService<ICorrelationContext>();
        using var _ = correlation.Push(Guid.NewGuid().ToString("N"));

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

        var total = await ExpireAsync(batch, correlation.CorrelationId, nowUtc, ct);

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
            correlationId,
            (services, id) => ExpireSeatAsync(services, id, correlationId, nowUtc, ct)
        );
        total += await ExpireEachAsync(
            batch.AddOnIntentIds,
            "Add-on intent",
            correlationId,
            (services, id) => ExpireAddOnAsync(services, id, correlationId, nowUtc, ct)
        );
        total += await ExpireEachAsync(
            batch.RenewalIntentIds,
            "Renewal intent",
            correlationId,
            (services, id) => ExpireRenewalAsync(services, id, correlationId, nowUtc, ct)
        );
        total += await ExpireEachAsync(
            batch.UpgradeRequestIds,
            "Upgrade request",
            correlationId,
            (services, id) => ExpireUpgradeAsync(services, id, correlationId, nowUtc, ct)
        );
        return total;
    }

    // Cada ítem en su propio scope: un fallo deja sucio solo su DbContext.
    private async Task<int> ExpireEachAsync(
        IReadOnlyList<Guid> ids,
        string kind,
        string correlationId,
        Func<IServiceProvider, Guid, Task<bool>> expireOne
    )
    {
        var expired = 0;
        foreach (var id in ids)
        {
            using var itemScope = ScopeFactory.CreateScope();
            var services = itemScope.ServiceProvider;
            using var _ = services.GetRequiredService<ICorrelationContext>().Push(correlationId);
            var jobLogger = services.GetRequiredService<ILogger<CheckoutIntentExpirationJob>>();
            try
            {
                if (await expireOne(services, id))
                    expired++;
            }
            catch (DbUpdateConcurrencyException)
            {
                jobLogger.LogInformation("{Kind} {Id} changed by another writer; not expired.", kind, id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                jobLogger.LogError(ex, "{Kind} expiration failed for {Id}.", kind, id);
            }
        }
        return expired;
    }

    private static async Task<bool> ExpireSeatAsync(
        IServiceProvider services,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var intent = await db.SeatPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != SeatPurchaseIntentStatus.Pending)
            return false;

        if (intent.SaaSPaymentId is { } paymentId)
            await services.GetRequiredService<IHostedCheckoutCancellationClient>().CancelAsync(intent.TenantId, paymentId, ct);

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != SeatPurchaseIntentStatus.Expired)
            return false;

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        var bus = services.GetRequiredService<IMessageBus>();
        bus.TenantId = intent.TenantId.ToString();
        await bus.PublishAsync(
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
        IServiceProvider services,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var intent = await db.AddOnPurchaseIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != AddOnPurchaseIntentStatus.Pending)
            return false;

        if (intent.SaaSPaymentId is { } paymentId)
            await services.GetRequiredService<IHostedCheckoutCancellationClient>().CancelAsync(intent.TenantId, paymentId, ct);

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != AddOnPurchaseIntentStatus.Expired)
            return false;

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        var bus = services.GetRequiredService<IMessageBus>();
        bus.TenantId = intent.TenantId.ToString();
        await bus.PublishAsync(
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
        IServiceProvider services,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var intent = await db.SubscriptionRenewalIntents.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (intent is null || intent.Status != SubscriptionRenewalIntentStatus.Pending)
            return false;

        if (intent.SaaSPaymentId is { } paymentId)
            await services.GetRequiredService<IHostedCheckoutCancellationClient>().CancelAsync(intent.TenantId, paymentId, ct);

        var result = intent.Expire(nowUtc);
        if (result.IsFailure || intent.Status != SubscriptionRenewalIntentStatus.Expired)
            return false;

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        var bus = services.GetRequiredService<IMessageBus>();
        bus.TenantId = intent.TenantId.ToString();
        await bus.PublishAsync(
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
        IServiceProvider services,
        Guid id,
        string correlationId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var db = services.GetRequiredService<SubscriptionDbContext>();
        var request = await db
            .Subscriptions.IgnoreQueryFilters()
            .SelectMany(s => s.PlanChangeRequests)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || request.Status != PlanChangeRequestStatus.AwaitingPayment)
            return false;

        if (request.SaaSPaymentId is { } paymentId)
            await services.GetRequiredService<IHostedCheckoutCancellationClient>().CancelAsync(request.TenantId, paymentId, ct);

        var result = request.Expire(nowUtc);
        if (result.IsFailure || request.Status != PlanChangeRequestStatus.Expired)
            return false;

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        var bus = services.GetRequiredService<IMessageBus>();
        bus.TenantId = request.TenantId.ToString();
        await bus.PublishAsync(
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
