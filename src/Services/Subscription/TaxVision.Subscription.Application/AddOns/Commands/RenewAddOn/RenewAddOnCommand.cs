namespace TaxVision.Subscription.Application.AddOns.Commands.RenewAddOn;

/// <summary>Extensión sin cobro del período de un add-on. Solo soporte de plataforma.</summary>
public sealed record RenewAddOnCommand(Guid TenantId, Guid TenantAddOnId, Guid RequestedByUserId);
