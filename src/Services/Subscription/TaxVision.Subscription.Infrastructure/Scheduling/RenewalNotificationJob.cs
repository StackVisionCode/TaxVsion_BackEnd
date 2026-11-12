using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Una vez al día, avisa (vía evento — Notification decide plantilla y destinatario)
/// cuando faltan exactamente 7, 3 o 1 día(s) para una renovación. No reintenta ni
/// deduplica con una tabla de log: al correr una vez por día y comparar por día exacto,
/// cada umbral se cruza una sola vez de forma natural. Cada aviso sale por el outbox en su propia unidad (BB-3).
/// </summary>
public sealed class RenewalNotificationJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<RenewalNotificationJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
{
    // Dos avisos pre-vencimiento: una semana para que el admin reaccione, un día antes como última llamada.
    // Tres sería ruido y el banner en la UI ya cubre los días intermedios.
    private static readonly int[] NotifyThresholdDays = [7, 1];
    private const int BatchSize = 500;

    protected override string JobName => "renewal-notification";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var logger = services.GetRequiredService<ILogger<RenewalNotificationJob>>();
        // El job es el origen de la traza: un id por pasada, para seguir junto todo lo que publique.
        var correlationId = Guid.NewGuid().ToString("N");

        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddDays(NotifyThresholdDays[0] + 1);

        // El aviso lleva el nombre del plan (no su código) y el total de la renovación.
        var plans = (await services.GetRequiredService<IPlanRepository>().GetAllAsync(ct)).ToDictionary(plan =>
            plan.Id
        );
        var subscriptionNotices = await UpcomingSubscriptionRenewalsAsync(
            subscriptions,
            services.GetRequiredService<ITenantAddOnRepository>(),
            seats,
            plans,
            correlationId,
            nowUtc,
            nowUtc.AddDays(RenewalNotice.WindowDays),
            ct
        );
        var seatNotices = await UpcomingSeatRenewalsAsync(seats, correlationId, nowUtc, windowEndUtc, ct);
        var endingNotices = await AccessEndingAsync(subscriptions, correlationId, nowUtc, windowEndUtc, ct);

        await RunUnitsAsync(
            [.. subscriptionNotices, .. seatNotices, .. endingNotices],
            async (notice, unit, _) =>
            {
                await unit.PublishAsync(notice);
                return true;
            },
            ct
        );

        if (endingNotices.Count > 0)
            logger.LogInformation(
                "RenewalNotificationJob warned {Count} tenant(s) that their access ends soon.",
                endingNotices.Count
            );

        if (subscriptionNotices.Count + seatNotices.Count > 0)
        {
            logger.LogInformation(
                "RenewalNotificationJob notified {Subscriptions} subscription(s) and {Seats} seat(s).",
                subscriptionNotices.Count,
                seatNotices.Count
            );
        }
    }

    private static async Task<List<object>> UpcomingSubscriptionRenewalsAsync(
        ISubscriptionRepository subscriptions,
        ITenantAddOnRepository addOns,
        ISubscriptionSeatRepository seats,
        IReadOnlyDictionary<Guid, SubscriptionPlan> plans,
        string correlationId,
        DateTime nowUtc,
        DateTime windowEndUtc,
        CancellationToken ct
    )
    {
        var candidates = await subscriptions.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var notices = new List<object>();

        foreach (var subscription in candidates)
        {
            var daysUntilDue = DaysUntilDue(subscription.NextRenewalAtUtc!.Value, nowUtc);
            // Anual: 30, 7 y 1 días; mensual: 7 y 1.
            if (!RenewalNotice.ShouldNotify(subscription.BillingCycle, daysUntilDue))
                continue;

            var plan = plans.GetValueOrDefault(subscription.PlanId);
            var version = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
            var planPrice = version is null
                ? null
                : PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
            long? totalCents = planPrice is { } price
                ? RenewalNotice.TotalCents(
                    price.AmountCents,
                    await addOns.GetByTenantIdAsync(subscription.TenantId, ct),
                    await seats.GetByTenantIdAsync(subscription.TenantId, ct)
                )
                : null;

            notices.Add(
                new SubscriptionRenewalUpcomingIntegrationEvent
                {
                    TenantId = subscription.TenantId,
                    CorrelationId = correlationId,
                    TenantSubscriptionId = subscription.Id,
                    DueAtUtc = subscription.NextRenewalAtUtc.Value,
                    DaysUntilDue = daysUntilDue,
                    PlanCode = subscription.PlanCode,
                    PlanName = plan?.Name,
                    AmountCents = totalCents,
                    Currency = planPrice?.Currency,
                    BillingCycle = subscription.BillingCycle.ToString(),
                }
            );
        }

        return notices;
    }

    /// <summary>
    /// Recordatorio de que se acerca el fin del acceso de una cancelación programada. Viaja por el canal de
    /// ciclo de vida (mismo destinatario, mismo envío) con motivo <see cref="SubscriptionChangeReason.AccessEnding"/>;
    /// la suscripción sigue Active, así que no hay transición real que publicar.
    /// </summary>
    private static async Task<List<object>> AccessEndingAsync(
        ISubscriptionRepository subscriptions,
        string correlationId,
        DateTime nowUtc,
        DateTime windowEndUtc,
        CancellationToken ct
    )
    {
        var candidates = await subscriptions.GetAccessEndingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var notices = new List<object>();

        foreach (var subscription in candidates)
        {
            if (Array.IndexOf(NotifyThresholdDays, DaysUntilDue(subscription.CurrentPeriodEndUtc, nowUtc)) < 0)
                continue;

            notices.Add(
                SubscriptionStatusChangedPublishing.Reannouncement(
                    subscription,
                    SubscriptionChangeReason.AccessEnding,
                    correlationId
                )
            );
        }

        return notices;
    }

    private static async Task<List<object>> UpcomingSeatRenewalsAsync(
        ISubscriptionSeatRepository seats,
        string correlationId,
        DateTime nowUtc,
        DateTime windowEndUtc,
        CancellationToken ct
    )
    {
        var candidates = await seats.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var notices = new List<object>();

        foreach (var seat in candidates)
        {
            var daysUntilDue = DaysUntilDue(seat.NextRenewalAtUtc!.Value, nowUtc);
            if (Array.IndexOf(NotifyThresholdDays, daysUntilDue) < 0)
                continue;

            notices.Add(
                new SeatRenewalUpcomingIntegrationEvent
                {
                    TenantId = seat.TenantId,
                    CorrelationId = correlationId,
                    SeatId = seat.Id,
                    DueAtUtc = seat.NextRenewalAtUtc.Value,
                    DaysUntilDue = daysUntilDue,
                }
            );
        }

        return notices;
    }

    private static int DaysUntilDue(DateTime dueAtUtc, DateTime nowUtc) => (dueAtUtc.Date - nowUtc.Date).Days;
}
