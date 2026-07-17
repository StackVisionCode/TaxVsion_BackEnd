using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.SeatAssignments.Commands.ReleaseSeatFromUser;

public static class ReleaseSeatFromUserHandler
{
    public static async Task<Result> Handle(
        ReleaseSeatFromUserCommand command,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure(new Error("Seat.NotFound", "Seat does not exist."));

        var releasedUserId = seat.CurrentUserId;
        var nowUtc = DateTime.UtcNow;

        var result = seat.ReleaseCurrentAssignment(command.ActorUserId, nowUtc, command.Reason);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(
            new SeatReleasedFromUserIntegrationEvent
            {
                TenantId = command.TenantId,
                SeatId = seat.Id,
                UserId = releasedUserId!.Value,
                ReleasedByUserId = command.ActorUserId,
                ReleaseReason = command.Reason,
                ReleasedAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "SubscriptionSeat",
            seat.Id,
            "Seat.Released",
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { CurrentUserId = releasedUserId },
            after: new { CurrentUserId = seat.CurrentUserId },
            reason: command.Reason,
            nowUtc,
            ct
        );

        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Seat {SeatId} released from user {UserId} for tenant {TenantId}.",
            seat.Id,
            releasedUserId,
            command.TenantId
        );
        return Result.Success();
    }
}
