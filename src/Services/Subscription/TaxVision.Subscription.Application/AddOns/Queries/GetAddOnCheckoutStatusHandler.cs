using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.AddOns.Queries;

/// <summary>Estado de una compra de add-on por checkout: el Landing lo pollea al volver del proveedor, hasta
/// que el webhook la activa.</summary>
public static class GetAddOnCheckoutStatusHandler
{
    public static async Task<Result<AddOnCheckoutStatusResponse>> Handle(
        GetAddOnCheckoutStatusQuery query,
        IAddOnPurchaseIntentRepository intents,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(query.IntentId, query.TenantId, ct);
        if (intent is null)
            return Result.Failure<AddOnCheckoutStatusResponse>(
                new Error("AddOnPurchaseIntent.NotFound", "Checkout intent does not exist.")
            );

        return Result.Success(
            new AddOnCheckoutStatusResponse(
                intent.Id,
                intent.Status.ToString(),
                intent.AddOnCode,
                intent.Quantity,
                intent.ProratedTotalCents,
                intent.Currency,
                intent.CheckoutUrl
            )
        );
    }
}
