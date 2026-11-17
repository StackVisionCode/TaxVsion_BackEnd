using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.AddOns.Queries;

/// <summary>Vista previa de la compra de un add-on (storage en el CRM) antes de pagar. No crea nada.</summary>
public sealed record GetAddOnQuoteQuery(Guid TenantId, string AddOnCode, int Quantity);

/// <summary>
/// Lo mismo que cobraría el checkout ahora: prorrateo hasta el fin del período (<see cref="GrossTotalCents"/>), el
/// descuento vigente si cubre add-ons y el neto. <see cref="DiscountPercentBasisPoints"/> o
/// <see cref="DiscountAmountCents"/> describen el descuento para la etiqueta.
/// </summary>
public sealed record AddOnQuoteResponse(
    string AddOnCode,
    string Name,
    int Quantity,
    long GrossTotalCents,
    long? DiscountCents,
    long NetTotalCents,
    string Currency,
    int? DiscountPercentBasisPoints = null,
    long? DiscountAmountCents = null
);

public static class GetAddOnQuoteHandler
{
    public static async Task<Result<AddOnQuoteResponse>> Handle(
        GetAddOnQuoteQuery query,
        AddOnPurchaseEligibility eligibility,
        CancellationToken ct
    )
    {
        var eligible = await eligibility.EnsurePurchasableAsync(query.TenantId, query.AddOnCode, query.Quantity, ct);
        if (eligible.IsFailure)
            return Result.Failure<AddOnQuoteResponse>(eligible.Error);

        var (subscription, definition) = eligible.Value;
        var nowUtc = DateTime.UtcNow;
        var priced = AddOnCheckoutPricing.Price(subscription, definition, query.Quantity, nowUtc);
        if (priced.IsFailure)
            return Result.Failure<AddOnQuoteResponse>(priced.Error);

        var charge = priced.Value.Charge;
        var discount = charge.DiscountCents is null ? null : subscription.ActiveDiscount(nowUtc);
        return Result.Success(
            new AddOnQuoteResponse(
                definition.Code.Value,
                definition.Name,
                query.Quantity,
                charge.GrossCents,
                charge.DiscountCents,
                charge.NetCents,
                priced.Value.UnitPrice.Currency,
                discount?.PercentBasisPoints,
                discount?.AmountCents
            )
        );
    }
}
