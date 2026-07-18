using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.IntegrationEvents;

public static class SeatRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        SeatRenewalPaymentFailedIntegrationEvent evt,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var seat = await seats.GetByIdAsync(evt.SeatId, evt.TenantId, ct);
            if (seat is null)
            {
                logger.LogWarning("SeatRenewalPaymentFailed for unknown seat {SeatId}.", evt.SeatId);
                return;
            }

            var renewal = FindRenewalByKey(seat, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning("SeatRenewalPaymentFailed for {SeatId} has no matching renewal for key {Key}.", evt.SeatId, evt.IdempotencyKey);
                return;
            }

            var result = seat.FailRenewal(
                renewal.Id, evt.FailureCode, evt.FailureReason, evt.WillRetry, evt.NextRetryAtUtc, actorUserId: Guid.Empty, DateTime.UtcNow);
            if (result.IsFailure)
            {
                logger.LogWarning("Could not record failed renewal for seat {SeatId}: {Code}.", seat.Id, result.Error.Code);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static SubscriptionSeatRenewal? FindRenewalByKey(SubscriptionSeat seat, string idempotencyKey)
    {
        foreach (var renewal in seat.Renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }
}
