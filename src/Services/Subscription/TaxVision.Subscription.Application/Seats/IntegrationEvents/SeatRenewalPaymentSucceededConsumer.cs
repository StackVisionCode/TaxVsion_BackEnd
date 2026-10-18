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

/// <summary>Cierra el loop de renovación de un seat — independiente de la suscripción base
/// y de otros seats, igual que <see cref="SubscriptionSeat.BeginRenewal"/> lo es.</summary>
public static class SeatRenewalPaymentSucceededConsumer
{
    public static async Task Handle(
        SeatRenewalPaymentSucceededIntegrationEvent evt,
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

            // El mismo evento sirve para 2 flujos distintos: renovación automática (seat Active, dunning
            // periódico) y reactivación manual (admin pagó un seat Suspended/PastDue vía
            // StartSeatReactivationCheckout). El estado del seat decide a cuál despachar: CompleteRenewal
            // exige Active, CompleteReactivation acepta Suspended/PastDue y transiciona a Active con nuevo
            // período. Así no multiplicamos eventos de integración para una misma intención ("pago de seat").
            var isReactivation = seat.Status is SeatStatus.Suspended or SeatStatus.PastDue;
            var result = isReactivation
                ? seat.CompleteReactivation(
                    renewal.Id,
                    evt.ExternalPaymentReference,
                    actorUserId: Guid.Empty,
                    evt.PaidAtUtc
                )
                : seat.CompleteRenewal(
                    renewal.Id,
                    evt.ExternalPaymentReference,
                    actorUserId: Guid.Empty,
                    evt.PaidAtUtc
                );
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Could not {Op} for seat {SeatId}: {Code}.",
                    isReactivation ? "reactivate" : "complete renewal",
                    seat.Id,
                    result.Error.Code
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            if (isReactivation)
            {
                await bus.PublishAsync(
                    new SubscriptionSeatReactivatedIntegrationEvent
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
