using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.RenewSeat;

public static class RenewSeatHandler
{
    public static async Task<Result> Handle(
        RenewSeatCommand command,
        ISubscriptionSeatRepository seats,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionSeat> logger,
        CancellationToken ct
    )
    {
        var seat = await seats.GetByIdAsync(command.SeatId, command.TenantId, ct);
        if (seat is null)
            return Result.Failure(new Error("Seat.NotFound", "Seat does not exist."));

        // D-A: termina junto con el plan activo, como la renovación del job.
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        var planEndUtc = subscription is { Status: SubscriptionStatus.Active }
            ? subscription.CurrentPeriodEndUtc
            : (DateTime?)null;
        var result = BeginAndCompleteRenewal(seat, planEndUtc, command.RequestedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Seat {SeatId} renewed without charge by platform support {UserId}.",
            seat.Id,
            command.RequestedByUserId
        );
        return Result.Success();
    }

    private static Result BeginAndCompleteRenewal(SubscriptionSeat seat, DateTime? planEndUtc, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;
        if (seat.CurrentPeriodEndUtc is not { } periodEndUtc)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot renew a seat in status {seat.Status}."));

        var idempotencyKey = IdempotencyKeyFactory.SeatRenewal(seat.Id, periodEndUtc);
        var next = SeatAlignment.ForRenewal(seat.UnitPrice, seat.BillingCycle, periodEndUtc, planEndUtc);
        var beginResult = seat.BeginRenewal(idempotencyKey, next.PeriodEndUtc, actorUserId, nowUtc);
        if (beginResult.IsFailure)
            return beginResult;

        var renewal = FindRenewalByKey(seat, idempotencyKey);
        if (renewal is null)
            return Result.Failure(new Error("Seat.RenewalNotFound", "Renewal was not scheduled."));

        return seat.CompleteRenewal(
            renewal.Id,
            externalPaymentReference: "platform-support-renewal",
            actorUserId,
            nowUtc
        );
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
