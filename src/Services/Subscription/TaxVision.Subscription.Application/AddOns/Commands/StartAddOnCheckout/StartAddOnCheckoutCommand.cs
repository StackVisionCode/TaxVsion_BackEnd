namespace TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;

/// <summary>Compra de un add-on por checkout hosteado (tenant sin método en archivo). El ciclo no se pide: el
/// add-on hereda el del plan, igual que en la compra off-session.</summary>
public sealed record StartAddOnCheckoutCommand(
    Guid TenantId,
    string AddOnCode,
    int Quantity,
    bool AutoRenew,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string Provider,
    string Method,
    Guid RequestedByUserId
);

public sealed record StartAddOnCheckoutResponse(
    Guid AddOnPurchaseIntentId,
    string CheckoutUrl,
    Guid PaymentId,
    DateTime ExpiresAtUtc
);
