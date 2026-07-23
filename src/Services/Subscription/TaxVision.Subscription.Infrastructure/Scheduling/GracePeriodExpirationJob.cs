using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Suspende suscripciones base, seats y add-ons cuyo grace period venció sin que la
/// renovación se recuperara. Cada aggregate se procesa de forma independiente — un seat en
/// grace no arrastra a la suscripción base ni a otros seats.
/// </summary>
public sealed class GracePeriodExpirationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<GracePeriodExpirationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "grace-period-expiration";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var logger = services.GetRequiredService<ILogger<GracePeriodExpirationJob>>();

        var subscriptionsSuspended = await SuspendExpiredSubscriptionsAsync(services, nowUtc, logger, ct);
        var seatsSuspended = await SuspendExpiredSeatsAsync(services, nowUtc, logger, ct);
        var addOnsSuspended = await SuspendExpiredAddOnsAsync(services, nowUtc, logger, ct);

        if (subscriptionsSuspended + seatsSuspended + addOnsSuspended > 0)
        {
            logger.LogInformation(
                "GracePeriodExpirationJob suspended {Subscriptions} subscription(s), {Seats} seat(s), {AddOns} add-on(s).",
                subscriptionsSuspended,
                seatsSuspended,
                addOnsSuspended
            );
        }
    }

    private static async Task<int> SuspendExpiredSubscriptionsAsync(
        IServiceProvider services,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var due = await subscriptions.GetPastGracePeriodAsync(nowUtc, BatchSize, ct);
        var count = 0;
        foreach (var subscription in due)
        {
            var result = subscription.SuspendBecauseGraceExpired(actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
                continue;

            await unitOfWork.SaveChangesAsync(ct);

            // RBAC Fase 5 — RecalculateEntitlementsSafelyAsync despacha vía bus.InvokeAsync a un
            // scope Wolverine nuevo; sin este stamp LocalCommandTenantMiddleware no tiene tenant
            // que restaurar y el filtro fail-closed de SubscriptionDbContext bloquearía el handler.
            bus.TenantId = subscription.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(subscription.TenantId, logger, ct);
            count++;
        }

        return count;
    }

    private static async Task<int> SuspendExpiredSeatsAsync(
        IServiceProvider services,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var due = await seats.GetPastGracePeriodAsync(nowUtc, BatchSize, ct);
        var count = 0;
        foreach (var seat in due)
        {
            var result = seat.SuspendBecauseGraceExpired(actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
                continue;

            await unitOfWork.SaveChangesAsync(ct);

            // RBAC Fase 5 — RecalculateEntitlementsSafelyAsync despacha vía bus.InvokeAsync a un
            // scope Wolverine nuevo; sin este stamp LocalCommandTenantMiddleware no tiene tenant
            // que restaurar y el filtro fail-closed de SubscriptionDbContext bloquearía el handler.
            bus.TenantId = seat.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(seat.TenantId, logger, ct);
            count++;
        }

        return count;
    }

    private static async Task<int> SuspendExpiredAddOnsAsync(
        IServiceProvider services,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var tenantAddOns = services.GetRequiredService<ITenantAddOnRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var due = await tenantAddOns.GetPastGracePeriodAsync(nowUtc, BatchSize, ct);
        var count = 0;
        foreach (var addOn in due)
        {
            var result = addOn.SuspendBecauseGraceExpired(actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
                continue;

            await unitOfWork.SaveChangesAsync(ct);

            // RBAC Fase 5 — RecalculateEntitlementsSafelyAsync despacha vía bus.InvokeAsync a un
            // scope Wolverine nuevo; sin este stamp LocalCommandTenantMiddleware no tiene tenant
            // que restaurar y el filtro fail-closed de SubscriptionDbContext bloquearía el handler.
            bus.TenantId = addOn.TenantId.ToString();
            await bus.RecalculateEntitlementsSafelyAsync(addOn.TenantId, logger, ct);
            count++;
        }

        return count;
    }
}
