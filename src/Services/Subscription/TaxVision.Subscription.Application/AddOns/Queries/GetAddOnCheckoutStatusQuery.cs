namespace TaxVision.Subscription.Application.AddOns.Queries;

public sealed record GetAddOnCheckoutStatusQuery(Guid TenantId, Guid IntentId);

public sealed record AddOnCheckoutStatusResponse(
    Guid AddOnPurchaseIntentId,
    string Status,
    string AddOnCode,
    int Quantity,
    long ProratedTotalCents,
    string Currency,
    string? CheckoutUrl,
    // Con qué proveedor se abrió (Stripe | PayPal); null en las de antes.
    string? CheckoutProvider = null
);
