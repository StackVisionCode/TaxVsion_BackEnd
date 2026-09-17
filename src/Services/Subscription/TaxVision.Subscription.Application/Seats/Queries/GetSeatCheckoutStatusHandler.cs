using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Seats.Queries;

public static class GetSeatCheckoutStatusHandler
{
    public static async Task<Result<SeatCheckoutStatusResponse>> Handle(
        GetSeatCheckoutStatusQuery query,
        ISeatPurchaseIntentRepository intents,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(query.IntentId, query.TenantId, ct);
        return intent is null
            ? Result.Failure<SeatCheckoutStatusResponse>(
                new Error("SeatPurchaseIntent.NotFound", "Checkout intent does not exist.")
            )
            : Result.Success(
                new SeatCheckoutStatusResponse(
                    intent.Id,
                    intent.Status.ToString(),
                    intent.SeatType.ToString(),
                    intent.Quantity,
                    intent.ProratedTotalCents,
                    intent.Currency,
                    intent.CheckoutUrl
                )
            );
    }
}
