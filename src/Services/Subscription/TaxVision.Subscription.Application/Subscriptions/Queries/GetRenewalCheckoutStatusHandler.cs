using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>Estado de una intención de renovación self-service — el front lo pollea hasta <c>Provisioned</c>/
/// <c>Failed</c> tras volver del checkout. Molde: <c>GetSeatCheckoutStatusHandler</c>.</summary>
public sealed record GetRenewalCheckoutStatusQuery(Guid TenantId, Guid IntentId);

public sealed record RenewalCheckoutStatusResponse(
    Guid RenewalIntentId,
    string Status,
    long AmountCents,
    string Currency,
    string? CheckoutUrl
);

public static class GetRenewalCheckoutStatusHandler
{
    public static async Task<Result<RenewalCheckoutStatusResponse>> Handle(
        GetRenewalCheckoutStatusQuery query,
        IRenewalCheckoutIntentRepository intents,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(query.IntentId, query.TenantId, ct);
        return intent is null
            ? Result.Failure<RenewalCheckoutStatusResponse>(
                new Error("SubscriptionRenewalIntent.NotFound", "Checkout intent does not exist.")
            )
            : Result.Success(
                new RenewalCheckoutStatusResponse(
                    intent.Id,
                    intent.Status.ToString(),
                    intent.AmountCents,
                    intent.Currency,
                    intent.CheckoutUrl
                )
            );
    }
}
