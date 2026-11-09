using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>Lo que deja una compra directa de add-on: el add-on y, si hay algo que cobrar, su cargo inicial.</summary>
public sealed record AddOnPurchase(TenantAddOn AddOn, InitialCharge? Charge);

/// <summary>Reglas de la compra de un add-on: elegibilidad propia de la suscripción, precio, co-término y cargo.</summary>
public static class AddOnPurchasePolicy
{
    public static Result EnsureValidQuantity(int quantity) =>
        quantity < 1
            ? Result.Failure(new Error("AddOn.InvalidQuantity", "Quantity must be at least 1."))
            : Result.Success();

    public static Result EnsureSubscriptionAllows(TenantSubscription subscription) =>
        subscription.AcceptsPurchases
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Subscription.CannotPurchaseAddOns",
                    $"Cannot purchase add-ons while subscription is {subscription.Status}."
                )
            );

    /// <param name="subscription">Suscripción base. Serializa compras simultáneas por su RowVersion.</param>
    /// <param name="definition">Catálogo del add-on.</param>
    /// <param name="quantity">Cantidad pedida.</param>
    /// <param name="autoRenew">Si renueva solo.</param>
    /// <param name="requestedByUserId">Quién compra.</param>
    /// <param name="nowUtc">Instante de la compra.</param>
    /// <param name="requestedAddOnId">Id derivado de la clave de idempotencia del cliente, si vino.</param>
    /// <param name="chargeKey">Clave de idempotencia del cargo por add-on e inicio de período.</param>
    public static Result<AddOnPurchase> Purchase(
        TenantSubscription subscription,
        AddOnDefinition definition,
        int quantity,
        bool autoRenew,
        Guid requestedByUserId,
        DateTime nowUtc,
        Guid? requestedAddOnId,
        Func<Guid, DateTime, string> chargeKey
    )
    {
        // Co-terminación: el add-on hereda el ciclo del plan y su precio se resuelve para ese ciclo.
        var billingCycle = subscription.BillingCycle;
        var unitPrice = definition.ResolveUnitPrice(billingCycle, quantity);
        if (unitPrice.IsFailure)
            return Result.Failure<AddOnPurchase>(unitPrice.Error);

        var addOnResult = TenantAddOn.Purchase(
            subscription.TenantId,
            definition,
            quantity,
            unitPrice.Value,
            billingCycle,
            autoRenew,
            requestedByUserId,
            nowUtc,
            requestedAddOnId
        );
        if (addOnResult.IsFailure)
            return Result.Failure<AddOnPurchase>(addOnResult.Error);

        var addOn = addOnResult.Value;

        // El primer período termina con el de la base; su cargo se proratea abajo.
        var coTerm = addOn.CoTermTo(subscription.CurrentPeriodEndUtc, requestedByUserId, nowUtc);
        if (coTerm.IsFailure)
            return Result.Failure<AddOnPurchase>(coTerm.Error);

        var charge = PrepareInitialCharge(addOn, subscription, requestedByUserId, nowUtc, chargeKey);
        if (charge.IsFailure)
            return Result.Failure<AddOnPurchase>(charge.Error);

        // Serializa compras simultáneas del tenant por la RowVersion de la suscripción.
        var recorded = subscription.RecordPurchase(requestedByUserId, nowUtc);
        if (recorded.IsFailure)
            return Result.Failure<AddOnPurchase>(recorded.Error);

        return Result.Success(new AddOnPurchase(addOn, charge.Value));
    }

    // Cargo del período parcial inicial, prorrateado por días sobre el período vigente de la base.
    // La base no se proratea; esto aplica solo al add-on. Devuelve null si no hay nada que cobrar.
    private static Result<InitialCharge?> PrepareInitialCharge(
        TenantAddOn addOn,
        TenantSubscription subscription,
        Guid actorUserId,
        DateTime nowUtc,
        Func<Guid, DateTime, string> chargeKey
    )
    {
        var prorated = ProrationCalculator.InitialPeriod(
            addOn.UnitPrice,
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            nowUtc
        );
        if (prorated.IsFailure)
            return Result.Failure<InitialCharge?>(prorated.Error);

        if (prorated.Value.Amount <= 0m)
            return Result.Success<InitialCharge?>(null);

        var key = chargeKey(addOn.Id, addOn.CurrentPeriodStartUtc);
        var begun = addOn.BeginInitialCharge(key, actorUserId, nowUtc);
        if (begun.IsFailure)
            return Result.Failure<InitialCharge?>(begun.Error);

        return Result.Success<InitialCharge?>(InitialCharge.From(key, prorated.Value));
    }
}
