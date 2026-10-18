using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.IntegrationEvents;

public static class SeatRenewalPaymentFailedConsumer
{
    public static async Task Handle(
        SeatRenewalPaymentFailedIntegrationEvent evt,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
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
                logger.LogWarning("SeatRenewalPaymentFailed for unknown seat {SeatId}.", evt.SeatId);
                return;
            }

            var renewal = FindRenewalByKey(seat, evt.IdempotencyKey);
            if (renewal is null)
            {
                logger.LogWarning(
                    "SeatRenewalPaymentFailed for {SeatId} has no matching renewal for key {Key}.",
                    evt.SeatId,
                    evt.IdempotencyKey
                );
                return;
            }

            var previousStatus = seat.Status;
            var nowUtc = DateTime.UtcNow;
            var result = seat.FailRenewal(
                renewal.Id,
                evt.FailureCode,
                evt.FailureReason,
                evt.WillRetry,
                evt.NextRetryAtUtc,
                actorUserId: Guid.Empty,
                nowUtc
            );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not record failed renewal for seat {SeatId}: {Code}.",
                    seat.Id,
                    result.Error.Code
                );
                return;
            }

            if (seat.Status == SeatStatus.PastDue && previousStatus != SeatStatus.PastDue)
            {
                var suspendResult = seat.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
                if (suspendResult.IsFailure)
                {
                    logger.LogWarning(
                        "Could not suspend seat {SeatId} after retries exhausted: {Code}.",
                        seat.Id,
                        suspendResult.Error.Code
                    );
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
            if (seat.Status == SeatStatus.Suspended && previousStatus != SeatStatus.Suspended)
            {
                await bus.PublishAsync(
                    new SubscriptionSeatSuspendedIntegrationEvent
                    {
                        TenantId = seat.TenantId,
                        SeatId = seat.Id,
                        CorrelationId = correlationId,
                    }
                );
                await bus.RecalculateEntitlementsSafelyAsync(seat.TenantId, logger, ct);
            }
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
