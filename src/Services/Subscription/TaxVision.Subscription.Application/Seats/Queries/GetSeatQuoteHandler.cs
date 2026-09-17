using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class GetSeatQuoteHandler
{
    public static async Task<Result<SeatQuoteResponse>> Handle(
        GetSeatQuoteQuery query,
        ISubscriptionRepository subscriptions,
        ISeatPricingRepository seatPricing,
        CancellationToken ct
    )
    {
        if (query.Quantity is < 1 or > 500)
            return Result.Failure<SeatQuoteResponse>(
                new Error("Seat.InvalidQuantity", "Quantity must be between 1 and 500.")
            );

        if (!Enum.TryParse<SeatType>(query.SeatType, ignoreCase: true, out var seatType))
            return Result.Failure<SeatQuoteResponse>(new Error("Seat.InvalidType", "Unknown seat type."));

        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<SeatQuoteResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var pricing = await seatPricing.GetAsync(ct);
        if (pricing is null)
            return Result.Failure<SeatQuoteResponse>(
                new Error("SeatPricing.NotFound", "Seat pricing catalog does not exist.")
            );

        var unitPrice = pricing.ResolveUnitPrice(seatType, subscription.BillingCycle);
        if (unitPrice.IsFailure)
            return Result.Failure<SeatQuoteResponse>(unitPrice.Error);

        // Mismo prorrateo que PurchaseSeatsHandler: por días sobre el período vigente de la base.
        var prorated = ProrationCalculator.InitialPeriod(
            unitPrice.Value,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            DateTime.UtcNow
        );
        if (prorated.IsFailure)
            return Result.Failure<SeatQuoteResponse>(prorated.Error);

        var proratedUnitCents = ToCents(prorated.Value.Amount);
        return Result.Success(
            new SeatQuoteResponse(
                seatType.ToString(),
                query.Quantity,
                subscription.BillingCycle.ToString(),
                ToCents(unitPrice.Value.Amount),
                proratedUnitCents,
                proratedUnitCents * query.Quantity,
                unitPrice.Value.Currency,
                subscription.CurrentPeriodEndUtc
            )
        );
    }

    private static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
