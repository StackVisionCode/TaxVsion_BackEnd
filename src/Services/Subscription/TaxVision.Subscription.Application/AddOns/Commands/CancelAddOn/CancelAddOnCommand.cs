namespace TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;

public sealed record CancelAddOnCommand(Guid TenantId, Guid TenantAddOnId, string Reason, Guid RequestedByUserId);
