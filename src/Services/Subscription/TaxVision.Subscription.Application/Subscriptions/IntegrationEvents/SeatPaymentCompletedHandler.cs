using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

public static class SeatPaymentCompletedHandler
{
    public static async Task Handle(
        SeatPaymentCompletedIntegrationEvent evt,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(evt.SubscriptionId, ct);
        if (subscription is null) return;

        var confirmResult = subscription.ConfirmSeat(evt.SeatId, evt.InvoiceId);
        if (confirmResult.IsFailure) return; // idempotente

        var seat = subscription.Seats.First(s => s.Id == evt.SeatId);

        // Programar la renovación PROPIA de este seat (ciclo independiente)
        await bus.ScheduleAsync(
            new SeatRenewalDueIntegrationEvent
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                SeatId = seat.Id,
                ExpectedPeriodEnd = seat.PeriodEndUtc,
                BillingAnchorDay = seat.BillingAnchorDay,
                CorrelationId = correlation.CorrelationId
            },
            seat.PeriodEndUtc);

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
