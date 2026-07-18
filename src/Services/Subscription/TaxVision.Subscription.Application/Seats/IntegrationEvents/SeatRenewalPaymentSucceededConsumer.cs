using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.IntegrationEvents;

/// <summary>Cierra el loop de renovación de un seat — independiente de la suscripción base
/// y de otros seats, igual que <see cref="SubscriptionSeat.BeginRenewal"/> lo es.</summary>
public static class SeatRenewalPaymentSucceededConsumer
{
    public static async Task Handle(
        SeatRenewalPaymentSucceededIntegrationEvent evt,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var seat = await seats.GetByIdAsync(evt.SeatId, evt.TenantId, ct);
            if (seat is null)
            {
                logger.LogWarning("SeatRenewalPaymentSucceeded for unknown seat {SeatId}.", evt.SeatId);
                return;
            }

            var renewal = FindRenewalByKey(seat, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "SeatRenewalPaymentSucceeded for {SeatId} has no matching renewal for key {Key}.",
                    evt.SeatId,
                    evt.IdempotencyKey
                );
                return;
            }

            var result = seat.CompleteRenewal(
                renewal.Id,
                evt.ExternalPaymentReference,
                actorUserId: Guid.Empty,
                evt.PaidAtUtc
            );
            if (result.IsFailure)
            {
                logger.LogWarning("Could not complete renewal for seat {SeatId}: {Code}.", seat.Id, result.Error.Code);
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
