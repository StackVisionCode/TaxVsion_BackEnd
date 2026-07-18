using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>Publica el intent de cobro cuando la suscripción base de un tenant llega a
/// NextRenewalAtUtc. No renueva seats ni add-ons. Si hay un downgrade agendado
/// (<see cref="PendingDowngrade"/>) para esta suscripción, lo aplica ANTES de resolver el
/// precio — así la renovación cobra normalmente el precio del plan nuevo, sin ningún job
/// separado ni prorrateo.</summary>
public sealed class TenantSubscriptionRenewalJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    ILogger<TenantSubscriptionRenewalJob> logger
) : PeriodicSubscriptionJob(scopeFactory, lockFactory, logger, TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
{
    private const int BatchSize = 200;

    protected override string JobName => "tenant-subscription-renewal";

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var subscriptions = services.GetRequiredService<ISubscriptionRepository>();
        var plans = services.GetRequiredService<IPlanRepository>();
        var bus = services.GetRequiredService<IMessageBus>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var logger = services.GetRequiredService<ILogger<TenantSubscriptionRenewalJob>>();

        var nowUtc = DateTime.UtcNow;
        var due = await subscriptions.GetDueForRenewalAsync(nowUtc, BatchSize, ct);

        foreach (var subscription in due)
        {
            await ApplyPendingDowngradeIfAnyAsync(subscription, plans, unitOfWork, bus, logger, nowUtc, ct);

            var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
            var planVersion = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
            var price = planVersion is null
                ? null
                : PlanPricing.ResolveBaseSubscriptionPrice(planVersion, subscription.BillingCycle);
            if (price is null)
            {
                logger.LogWarning(
                    "Could not resolve a price tier for subscription {SubscriptionId} (plan {PlanId}, version {PlanVersionId}, cycle {BillingCycle}); skipping renewal intent.",
                    subscription.Id,
                    subscription.PlanId,
                    subscription.PlanVersionId,
                    subscription.BillingCycle
                );
                continue;
            }

            var idempotencyKey = IdempotencyKeyFactory.SubscriptionRenewal(
                subscription.Id,
                subscription.CurrentPeriodEndUtc
            );
            var result = subscription.BeginRenewal(idempotencyKey, actorUserId: Guid.Empty, nowUtc);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not begin renewal for subscription {SubscriptionId}: {Code}.",
                    subscription.Id,
                    result.Error.Code
                );
                continue;
            }

            await unitOfWork.SaveChangesAsync(ct);

            await bus.PublishAsync(
                new SubscriptionRenewalDueIntegrationEvent
                {
                    TenantId = subscription.TenantId,
                    TenantSubscriptionId = subscription.Id,
                    PlanCode = subscription.PlanCode,
                    PeriodStartUtc = subscription.CurrentPeriodEndUtc,
                    PeriodEndUtc = subscription.BillingCycle.CalculateNext(subscription.CurrentPeriodEndUtc),
                    IdempotencyKey = idempotencyKey,
                    AmountCents = price.Value.AmountCents,
                    Currency = price.Value.Currency,
                }
            );
        }

        if (due.Count > 0)
            logger.LogInformation("TenantSubscriptionRenewalJob processed {Count} due subscription(s).", due.Count);
    }

    private static async Task ApplyPendingDowngradeIfAnyAsync(
        TenantSubscription subscription,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantSubscriptionRenewalJob> logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var pendingDowngrade = subscription.PendingDowngrades.FirstOrDefault(d =>
            d.Status == PendingDowngradeStatus.Scheduled
        );
        if (pendingDowngrade is null)
            return;

        var toPlan = await plans.GetByIdAsync(pendingDowngrade.ToPlanId, ct);
        var toPlanVersion = toPlan?.Versions.FirstOrDefault(v => v.Id == pendingDowngrade.ToPlanVersionId);
        if (toPlan is null || toPlanVersion is null)
        {
            logger.LogWarning(
                "Could not apply pending downgrade {Id} for subscription {SubscriptionId}: target plan/version no longer exists.",
                pendingDowngrade.Id,
                subscription.Id
            );
            return;
        }

        var applyResult = subscription.ApplyPendingDowngrade(
            pendingDowngrade.Id,
            toPlan,
            toPlanVersion,
            actorUserId: Guid.Empty,
            nowUtc
        );
        if (applyResult.IsFailure)
        {
            logger.LogWarning(
                "Could not apply pending downgrade {Id} for subscription {SubscriptionId}: {Code}.",
                pendingDowngrade.Id,
                subscription.Id,
                applyResult.Error.Code
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(subscription.TenantId, logger, ct);
    }
}
