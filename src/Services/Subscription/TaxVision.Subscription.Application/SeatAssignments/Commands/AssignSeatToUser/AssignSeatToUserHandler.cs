using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.SeatAssignments.Commands.AssignSeatToUser;

public static class AssignSeatToUserHandler
{
    public static async Task<Result> Handle(
        AssignSeatToUserCommand command,
        ISubscriptionSeatRepository seats,
        ISubscriptionTenantSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure(new Error("Seat.NotFound", "Seat does not exist."));

        var cooldownDays = (await settingsRepository.GetByTenantIdAsync(command.TenantId, ct))?.SeatReassignmentCooldownDays ?? 0;
        var nowUtc = DateTime.UtcNow;

        var result = seat.AssignTo(command.UserId, command.ActorUserId, nowUtc, cooldownDays);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(new SeatAssignedToUserIntegrationEvent
        {
            TenantId = command.TenantId,
            SeatId = seat.Id,
            UserId = command.UserId,
            AssignedByUserId = command.ActorUserId,
            SeatType = seat.Type.ToString(),
            AssignedAtUtc = nowUtc,
            SeatExpiresAtUtc = seat.CurrentPeriodEndUtc,
            CorrelationId = correlation.CorrelationId,
        });
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Seat {SeatId} assigned to user {UserId} for tenant {TenantId}.", seat.Id, command.UserId, command.TenantId);
        return Result.Success();
    }
}
