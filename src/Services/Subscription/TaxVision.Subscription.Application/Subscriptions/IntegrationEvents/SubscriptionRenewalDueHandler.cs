using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using Sub = TaxVision.Subscription.Domain.Subscriptions.Subscription;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Handles the base subscription renewal cycle.
/// Google Workspace model: charges the full cycle price at renewal — no proration.
/// Stale-check: only processes if PeriodEndUtc matches the expected date (prevents duplicate jobs).
/// </summary>
public static class SubscriptionRenewalDueHandler
{
    public static async Task Handle(
        SubscriptionRenewalDueIntegrationEvent evt,
        ISubscriptionRepository repo,
        IPlanRepository planRepo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        // Stale check — prevents double-charging if the event fires more than once
        if (subscription.PeriodEndUtc != evt.ExpectedPeriodEnd) return;

        if (subscription.Status == SubscriptionStatus.Cancelled) return;

        if (!subscription.IsAutoRenew)
        {
            subscription.Cancel();
            await uow.SaveChangesAsync(ct);
            return;
        }

        // Read current plan price at renewal time (not the price when subscribed)
        var plan = await planRepo.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null) return;

        var cyclePrice = plan.GetPriceForPeriod(subscription.BillingPeriod);
        var nextPeriodEnd = Sub.CalculateNextPeriodEnd(
            subscription.PeriodEndUtc, subscription.BillingPeriod, subscription.BillingAnchorDay);

        await bus.PublishAsync(new SubscriptionRenewalPaymentRequestedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            Amount = cyclePrice,
            Currency = plan.Currency,
            NewPeriodEnd = nextPeriodEnd,
            BillingAnchorDay = subscription.BillingAnchorDay,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}
