using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class SeatPaymentFailedHandler
{
    public static async Task Handle(
        SeatPaymentFailedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;
        subscription.CancelPendingSeat(evt.SeatId);
        await uow.SaveChangesAsync(ct);
    }
}
