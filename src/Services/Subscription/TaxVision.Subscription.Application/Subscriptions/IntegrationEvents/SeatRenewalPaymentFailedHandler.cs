using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class SeatRenewalPaymentFailedHandler
{
    public static async Task Handle(
        SeatRenewalPaymentFailedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        subscription.MarkSeatPastDue(evt.SeatId);

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
