namespace TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;

public sealed record PurchaseAddOnCommand(
    Guid TenantId,
    string AddOnCode,
    int Quantity,
    bool AutoRenew,
    Guid RequestedByUserId
);
