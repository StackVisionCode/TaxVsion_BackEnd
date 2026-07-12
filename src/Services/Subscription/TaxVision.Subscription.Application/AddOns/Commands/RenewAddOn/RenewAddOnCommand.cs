namespace TaxVision.Subscription.Application.AddOns.Commands.RenewAddOn;

/// <summary>Renovación manual de un add-on disparada por un admin, mientras no exista
/// integración con Billing.</summary>
public sealed record RenewAddOnCommand(Guid TenantId, Guid TenantAddOnId, Guid RequestedByUserId);
