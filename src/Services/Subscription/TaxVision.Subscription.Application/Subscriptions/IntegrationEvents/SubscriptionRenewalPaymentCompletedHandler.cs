using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.ValueObjects;
using Sub = TaxVision.Subscription.Domain.Subscriptions.Subscription;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>
/// Applies the renewal after Payment Service confirms the charge.
/// Advances the period and re-schedules the next renewal job.
/// </summary>
public static class SubscriptionRenewalPaymentCompletedHandler
{
    public static async Task Handle(
        SubscriptionRenewalPaymentCompletedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        var newPrice = new Money(evt.PricePerCycle, evt.Currency);
        var renewResult = subscription.RenewWithPayment(evt.InvoiceId, newPrice, evt.NewPeriodEnd);
        if (renewResult.IsFailure) return; // idempotente

        // Schedule next renewal
        await bus.ScheduleAsync(
            new SubscriptionRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                ExpectedPeriodEnd = evt.NewPeriodEnd,
                BillingAnchorDay = evt.BillingAnchorDay,
                CorrelationId = correlation.CorrelationId
            },
            evt.NewPeriodEnd);

        await bus.PublishAsync(new TenantEntitlementsChangedIntegrationEvent
        {
            TenantId = subscription.TenantId,
            SubscriptionId = subscription.Id,
            TotalAvailableSeats = subscription.TotalAvailableSeats,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);
    }
}
