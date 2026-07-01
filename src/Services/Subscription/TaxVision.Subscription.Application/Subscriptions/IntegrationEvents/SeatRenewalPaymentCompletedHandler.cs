using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class SeatRenewalPaymentCompletedHandler
{
    public static async Task Handle(
        SeatRenewalPaymentCompletedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        var newPrice = new Money(evt.PricePerSeat, evt.Currency);
        var renewResult = subscription.RenewSeat(evt.SeatId, evt.InvoiceId, evt.NewPeriodEnd, newPrice);
        if (renewResult.IsFailure) return;

        await bus.ScheduleAsync(
            new SeatRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                SeatId = evt.SeatId,
                ExpectedPeriodEnd = evt.NewPeriodEnd,
                BillingAnchorDay = evt.BillingAnchorDay,
                CorrelationId = correlation.CorrelationId
            },
            evt.NewPeriodEnd);

        await uow.SaveChangesAsync(ct);
    }
}
