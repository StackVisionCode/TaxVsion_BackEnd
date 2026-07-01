using TaxVision.Subscription.Domain.ValueObjects;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

// TenantId proviene del controller (extraído del JWT claim tenant_id)
public sealed record AddSeatCommand(Guid TenantId, int Quantity);

public sealed record AddSeatResponse(
    Guid SeatId,
    int Quantity,
    decimal PricePerSeat,
    decimal TotalAmount,
    string Currency,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int BillingAnchorDay,
    string Status);

public static class AddSeatHandler
{
    public static async Task<Result<AddSeatResponse>> Handle(
        AddSeatCommand cmd,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var subscription = await repo.GetActiveByTenantIdAsync(cmd.TenantId, ct);
        if (subscription is null)
            return Result.Failure<AddSeatResponse>(
                new Error("Subscription.NotFound", "No active subscription found."));

        var now = DateTime.UtcNow;
        var seatResult = subscription.RequestSeat(cmd.Quantity, now);
        if (seatResult.IsFailure)
            return Result.Failure<AddSeatResponse>(seatResult.Error);

        var seat = seatResult.Value;

        await bus.PublishAsync(new SeatPurchaseRequestedIntegrationEvent
        {
            TenantId = cmd.TenantId,
            SubscriptionId = subscription.Id,
            SeatId = seat.Id,
            Quantity = seat.Quantity,
            PricePerSeat = seat.PricePerSeat.Amount,
            TotalAmount = seat.TotalAmount.Amount,
            Currency = seat.TotalAmount.Currency,
            BillingPeriod = seat.BillingPeriod,
            PeriodStartUtc = seat.PeriodStartUtc,
            PeriodEndUtc = seat.PeriodEndUtc,
            BillingAnchorDay = seat.BillingAnchorDay,
            CorrelationId = correlation.CorrelationId
        });

        await uow.SaveChangesAsync(ct);

        return Result.Success(new AddSeatResponse(
            seat.Id, seat.Quantity, seat.PricePerSeat.Amount,
            seat.TotalAmount.Amount, seat.TotalAmount.Currency,
            seat.PeriodStartUtc, seat.PeriodEndUtc,
            seat.BillingAnchorDay, seat.Status.ToString()));
    }
}