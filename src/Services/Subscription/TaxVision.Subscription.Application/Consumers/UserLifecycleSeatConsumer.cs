using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Consumers;

// Libera el asiento comprado del usuario cuando Auth lo desactiva u offboardea. Antes NADIE lo hacía:
// el asiento quedaba consumido por un usuario inactivo (el "libera el asiento" del comentario de Auth
// solo aplicaba al cupo local, no al asiento comprado). Idempotente: sin asiento asignado, no hace nada.
public static class UserLifecycleSeatConsumer
{
    public static Task Handle(
        UserDeactivatedIntegrationEvent evt,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    ) =>
        ReleaseSeatForUserAsync(
            evt.TenantId,
            evt.UserId,
            Guid.Empty,
            "UserDeactivated",
            evt.CorrelationId,
            evt.EventId,
            seats,
            unitOfWork,
            bus,
            correlation,
            audit,
            logger,
            ct
        );

    public static Task Handle(
        UserOffboardedIntegrationEvent evt,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    ) =>
        ReleaseSeatForUserAsync(
            evt.TenantId,
            evt.UserId,
            evt.OffboardedByUserId ?? Guid.Empty,
            "UserOffboarded",
            evt.CorrelationId,
            evt.EventId,
            seats,
            unitOfWork,
            bus,
            correlation,
            audit,
            logger,
            ct
        );

    private static async Task ReleaseSeatForUserAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        string reason,
        string? correlationId,
        Guid eventId,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId))
        {
            var seat = await seats.GetTrackedByCurrentUserIdAsync(tenantId, userId, ct);
            if (seat is null)
                return; // ya liberado o nunca tuvo asiento: nada que hacer.

            var nowUtc = DateTime.UtcNow;
            var release = seat.ReleaseCurrentAssignment(actorUserId, nowUtc, reason);
            if (release.IsFailure)
            {
                logger.LogWarning("Seat release skipped for user {UserId}: {Error}.", userId, release.Error.Code);
                return;
            }

            await bus.PublishAsync(
                new SeatReleasedFromUserIntegrationEvent
                {
                    TenantId = tenantId,
                    SeatId = seat.Id,
                    UserId = userId,
                    ReleasedByUserId = actorUserId,
                    ReleaseReason = reason,
                    ReleasedAtUtc = nowUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );
            await unitOfWork.SaveChangesAsync(ct);

            await AuditEntryFactory.AppendAsync(
                audit,
                tenantId,
                "SubscriptionSeat",
                seat.Id,
                "Seat.Released",
                actorUserId,
                correlation.CorrelationId,
                before: new { CurrentUserId = (Guid?)userId },
                after: new { CurrentUserId = seat.CurrentUserId },
                reason: reason,
                nowUtc,
                ct
            );

            await bus.RecalculateEntitlementsSafelyAsync(tenantId, logger, ct);

            logger.LogInformation(
                "Seat {SeatId} released from user {UserId} (tenant {TenantId}) — {Reason}.",
                seat.Id,
                userId,
                tenantId,
                reason
            );
        }
    }
}
