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
        var cancellation = services.GetRequiredService<IHostedCheckoutCancellationClient>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var jobLogger = services.GetRequiredService<ILogger<CheckoutIntentExpirationJob>>();

        var nowUtc = DateTime.UtcNow;
        var total = 0;

        total += await SweepSeatAsync(db, cancellation, bus, unitOfWork, correlation, jobLogger, nowUtc, ct);
        total += await SweepAddOnAsync(db, cancellation, bus, unitOfWork, correlation, jobLogger, nowUtc, ct);
        total += await SweepRenewalAsync(db, cancellation, bus, unitOfWork, correlation, jobLogger, nowUtc, ct);
        total += await SweepUpgradeAsync(db, cancellation, bus, unitOfWork, correlation, jobLogger, nowUtc, ct);

        if (total > 50)
            jobLogger.LogWarning(
                "CheckoutIntentExpirationJob expired {Count} intents in a single tick — unusually high, check provider connectivity.",
                total
            );
        else if (total > 0)
            jobLogger.LogInformation("CheckoutIntentExpirationJob expired {Count} intent(s).", total);
    }

    private static async Task<int> SweepSeatAsync(
        SubscriptionDbContext db,
        IHostedCheckoutCancellationClient cancellation,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var batch = await db
            .SeatPurchaseIntents.IgnoreQueryFilters()
            .Where(i =>
                i.Status == SeatPurchaseIntentStatus.Pending
                && i.CheckoutExpiresAtUtc != null
                && i.CheckoutExpiresAtUtc < nowUtc
            )
            .OrderBy(i => i.CheckoutExpiresAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var intent in batch)
        {
            try
            {
                if (intent.SaaSPaymentId is { } paymentId)
                    await cancellation.CancelAsync(intent.TenantId, paymentId, ct);

                var result = intent.Expire(nowUtc);
                if (result.IsFailure || intent.Status != SeatPurchaseIntentStatus.Expired)
                    continue;

                await unitOfWork.SaveChangesAsync(ct);
                bus.TenantId = intent.TenantId.ToString();
                await bus.PublishAsync(
                    new SeatCheckoutExpiredIntegrationEvent
                    {
                        TenantId = intent.TenantId,
                        SeatPurchaseIntentId = intent.Id,
                        SaaSPaymentId = intent.SaaSPaymentId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Seat intent expiration failed for intent {IntentId}.", intent.Id);
            }
        }
        return expired;
    }

    private static async Task<int> SweepAddOnAsync(
        SubscriptionDbContext db,
        IHostedCheckoutCancellationClient cancellation,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var batch = await db
            .AddOnPurchaseIntents.IgnoreQueryFilters()
            .Where(i =>
                i.Status == AddOnPurchaseIntentStatus.Pending
                && i.CheckoutExpiresAtUtc != null
                && i.CheckoutExpiresAtUtc < nowUtc
            )
            .OrderBy(i => i.CheckoutExpiresAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var intent in batch)
        {
            try
            {
                if (intent.SaaSPaymentId is { } paymentId)
                    await cancellation.CancelAsync(intent.TenantId, paymentId, ct);

                var result = intent.Expire(nowUtc);
                if (result.IsFailure || intent.Status != AddOnPurchaseIntentStatus.Expired)
                    continue;

                await unitOfWork.SaveChangesAsync(ct);
                bus.TenantId = intent.TenantId.ToString();
                await bus.PublishAsync(
                    new AddOnCheckoutExpiredIntegrationEvent
                    {
                        TenantId = intent.TenantId,
                        AddOnPurchaseIntentId = intent.Id,
                        SaaSPaymentId = intent.SaaSPaymentId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Add-on intent expiration failed for intent {IntentId}.", intent.Id);
            }
        }
        return expired;
    }

    private static async Task<int> SweepRenewalAsync(
        SubscriptionDbContext db,
        IHostedCheckoutCancellationClient cancellation,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var batch = await db
            .SubscriptionRenewalIntents.IgnoreQueryFilters()
            .Where(i =>
                i.Status == SubscriptionRenewalIntentStatus.Pending
                && i.CheckoutExpiresAtUtc != null
                && i.CheckoutExpiresAtUtc < nowUtc
            )
            .OrderBy(i => i.CheckoutExpiresAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var intent in batch)
        {
            try
            {
                if (intent.SaaSPaymentId is { } paymentId)
                    await cancellation.CancelAsync(intent.TenantId, paymentId, ct);

                var result = intent.Expire(nowUtc);
                if (result.IsFailure || intent.Status != SubscriptionRenewalIntentStatus.Expired)
                    continue;

                await unitOfWork.SaveChangesAsync(ct);
                bus.TenantId = intent.TenantId.ToString();
                await bus.PublishAsync(
                    new SubscriptionRenewalCheckoutExpiredIntegrationEvent
                    {
                        TenantId = intent.TenantId,
                        RenewalIntentId = intent.Id,
                        SaaSPaymentId = intent.SaaSPaymentId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Renewal intent expiration failed for intent {IntentId}.", intent.Id);
            }
        }
        return expired;
    }

    private static async Task<int> SweepUpgradeAsync(
        SubscriptionDbContext db,
        IHostedCheckoutCancellationClient cancellation,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var batch = await db
            .Subscriptions.IgnoreQueryFilters()
            .SelectMany(s => s.PlanChangeRequests)
            .Where(r =>
                r.Status == PlanChangeRequestStatus.AwaitingPayment
                && r.CheckoutExpiresAtUtc != null
                && r.CheckoutExpiresAtUtc < nowUtc
            )
            .OrderBy(r => r.CheckoutExpiresAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var request in batch)
        {
            try
            {
                if (request.SaaSPaymentId is { } paymentId)
                    await cancellation.CancelAsync(request.TenantId, paymentId, ct);

                var result = request.Expire(nowUtc);
                if (result.IsFailure || request.Status != PlanChangeRequestStatus.Expired)
                    continue;

                await unitOfWork.SaveChangesAsync(ct);
                bus.TenantId = request.TenantId.ToString();
                await bus.PublishAsync(
                    new PlanChangeUpgradeExpiredIntegrationEvent
                    {
                        TenantId = request.TenantId,
                        PlanChangeRequestId = request.Id,
                        SaaSPaymentId = request.SaaSPaymentId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Upgrade request expiration failed for request {RequestId}.", request.Id);
            }
        }
        return expired;
    }
}
