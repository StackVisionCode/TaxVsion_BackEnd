using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.RenewSeat;

public static class RenewSeatHandler
{
    public static async Task<Result> Handle(
        RenewSeatCommand command,
        ISubscriptionSeatRepository seats,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure(new Error("Seat.NotFound", "Seat does not exist."));

        var result = BeginAndCompleteRenewal(seat, command.RequestedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation("Seat {SeatId} manually renewed (requested by {UserId}).", seat.Id, command.RequestedByUserId);
        return Result.Success();
    }

    private static Result BeginAndCompleteRenewal(SubscriptionSeat seat, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var idempotencyKey = IdempotencyKeyFactory.SeatRenewal(seat.Id, seat.CurrentPeriodEndUtc!.Value);

        var beginResult = seat.BeginRenewal(idempotencyKey, actorUserId, nowUtc);
        if (beginResult.IsFailure)
            return beginResult;

        var renewal = FindRenewalByKey(seat, idempotencyKey);
        if (renewal is null)
            return Result.Failure(new Error("Seat.RenewalNotFound", "Renewal was not scheduled."));

        return seat.CompleteRenewal(renewal.Id, externalPaymentReference: "manual-admin-renewal", actorUserId, nowUtc);
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
