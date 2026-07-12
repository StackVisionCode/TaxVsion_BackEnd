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

namespace TaxVision.Subscription.Application.SeatAssignments.Commands.ReassignSeat;

public static class ReassignSeatHandler
{
    public static async Task<Result> Handle(
        ReassignSeatCommand command,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
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

        var previousUserId = seat.CurrentUserId;
        var cooldownDays = (await settingsRepository.GetByTenantIdAsync(command.TenantId, ct))?.SeatReassignmentCooldownDays ?? 0;
        var nowUtc = DateTime.UtcNow;

        var result = seat.ReassignSeat(command.ToUserId, command.ActorUserId, nowUtc, command.Reason, cooldownDays);
        if (result.IsFailure)
            return result;

        if (previousUserId is not null)
        {
            await bus.PublishAsync(new SeatReleasedFromUserIntegrationEvent
            {
                TenantId = command.TenantId,
                SeatId = seat.Id,
                UserId = previousUserId.Value,
                ReleasedByUserId = command.ActorUserId,
                ReleaseReason = command.Reason,
                ReleasedAtUtc = nowUtc,
                CorrelationId = correlation.CorrelationId,
            });
        }

        await bus.PublishAsync(new SeatAssignedToUserIntegrationEvent
        {
            TenantId = command.TenantId,
            SeatId = seat.Id,
            UserId = command.ToUserId,
            AssignedByUserId = command.ActorUserId,
            SeatType = seat.Type.ToString(),
            AssignedAtUtc = nowUtc,
            SeatExpiresAtUtc = seat.CurrentPeriodEndUtc,
            CorrelationId = correlation.CorrelationId,
        });
        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, "SubscriptionSeat", seat.Id, "Seat.Reassigned",
            command.ActorUserId, correlation.CorrelationId,
            before: new { CurrentUserId = previousUserId },
            after: new { CurrentUserId = seat.CurrentUserId },
            reason: command.Reason, nowUtc, ct);

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation(
            "Seat {SeatId} reassigned from {PreviousUserId} to {NewUserId} for tenant {TenantId}.",
            seat.Id, previousUserId, command.ToUserId, command.TenantId
        );
        return Result.Success();
    }
}
