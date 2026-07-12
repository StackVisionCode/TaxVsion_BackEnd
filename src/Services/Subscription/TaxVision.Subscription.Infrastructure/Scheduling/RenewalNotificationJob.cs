using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Una vez al día, avisa (vía evento — Notification decide plantilla y destinatario)
/// cuando faltan exactamente 7, 3 o 1 día(s) para una renovación. No reintenta ni
/// deduplica con una tabla de log: al correr una vez por día y comparar por día exacto,
/// cada umbral se cruza una sola vez de forma natural.
/// </summary>
public sealed class RenewalNotificationJob(
    IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<RenewalNotificationJob> logger)
    : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(24), TimeSpan.FromHours(1))
{
    private static readonly int[] NotifyThresholdDays = [7, 3, 1];
    private const int BatchSize = 500;

    protected override string JobName => "renewal-notification";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var seats = services.GetRequiredService<ISubscriptionSeatRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var logger = services.GetRequiredService<ILogger<RenewalNotificationJob>>();

        var nowUtc = DateTime.UtcNow;
        var windowEndUtc = nowUtc.AddDays(NotifyThresholdDays[0] + 1);

        var subscriptionCount = await NotifyUpcomingSubscriptionRenewalsAsync(subscriptions, bus, nowUtc, windowEndUtc, ct);
        var seatCount = await NotifyUpcomingSeatRenewalsAsync(seats, bus, nowUtc, windowEndUtc, ct);

        if (subscriptionCount + seatCount > 0)
        {
            logger.LogInformation(
                "RenewalNotificationJob notified {Subscriptions} subscription(s) and {Seats} seat(s).", subscriptionCount, seatCount);
        }
    }

    private static async Task<int> NotifyUpcomingSubscriptionRenewalsAsync(
        ISubscriptionRepository subscriptions, IMessageBus bus, DateTime nowUtc, DateTime windowEndUtc, CancellationToken ct)
    {
        var candidates = await subscriptions.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var count = 0;

        foreach (var subscription in candidates)
        {
            var daysUntilDue = DaysUntilDue(subscription.NextRenewalAtUtc!.Value, nowUtc);
            if (Array.IndexOf(NotifyThresholdDays, daysUntilDue) < 0)
                continue;

            await bus.PublishAsync(new SubscriptionRenewalUpcomingIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                DueAtUtc = subscription.NextRenewalAtUtc.Value,
                DaysUntilDue = daysUntilDue,
                PlanCode = subscription.PlanCode,
            });
            count++;
        }

        return count;
    }

    private static async Task<int> NotifyUpcomingSeatRenewalsAsync(
        ISubscriptionSeatRepository seats, IMessageBus bus, DateTime nowUtc, DateTime windowEndUtc, CancellationToken ct)
    {
        var candidates = await seats.GetRenewingBetweenAsync(nowUtc, windowEndUtc, BatchSize, ct);
        var count = 0;

        foreach (var seat in candidates)
        {
            var daysUntilDue = DaysUntilDue(seat.NextRenewalAtUtc!.Value, nowUtc);
            if (Array.IndexOf(NotifyThresholdDays, daysUntilDue) < 0)
                continue;

            await bus.PublishAsync(new SeatRenewalUpcomingIntegrationEvent
            {
                TenantId = seat.TenantId,
                SeatId = seat.Id,
                DueAtUtc = seat.NextRenewalAtUtc.Value,
                DaysUntilDue = daysUntilDue,
                CurrentUserId = seat.CurrentUserId,
            });
            count++;
        }

        return count;
    }

    private static int DaysUntilDue(DateTime dueAtUtc, DateTime nowUtc) => (dueAtUtc.Date - nowUtc.Date).Days;
}
