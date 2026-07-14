using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro cuando la suscripción base de un tenant llega a
/// NextRenewalAtUtc. No renueva seats ni add-ons.</summary>
public sealed class TenantSubscriptionRenewalJob(
    IServiceScopeFactory scopeFactory, IDistributedLockFactory lockFactory, ILogger<TenantSubscriptionRenewalJob> logger)
    : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "tenant-subscription-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<TenantSubscriptionRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await subscriptions.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var subscription in due)
        {
            var idempotencyKey = IdempotencyKeyFactory.SubscriptionRenewal(subscription.Id, subscription.CurrentPeriodEndUtc);
            var result = subscription.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not begin renewal for subscription {SubscriptionId}: {Code}.", subscription.Id, result.Error.Code);
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(new SubscriptionRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                PlanCode = subscription.PlanCode,
                PeriodStartUtc = subscription.CurrentPeriodEndUtc,
                PeriodEndUtc = subscription.BillingCycle.CalculateNext(subscription.CurrentPeriodEndUtc),
                IdempotencyKey = idempotencyKey,
            });
        }

        if (due.Count > 0)
            logger.LogInformation("TenantSubscriptionRenewalJob processed {Count} due subscription(s).", due.Count);
    }
}
