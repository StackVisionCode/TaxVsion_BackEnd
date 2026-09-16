namespace TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;

// El ciclo de facturación no se pide: el add-on hereda el del plan (co-terminación).
public sealed record PurchaseAddOnCommand(
    Guid TenantId,
    string AddOnCode,
    int Quantity,
    bool AutoRenew,
    Guid RequestedByUserId
);
