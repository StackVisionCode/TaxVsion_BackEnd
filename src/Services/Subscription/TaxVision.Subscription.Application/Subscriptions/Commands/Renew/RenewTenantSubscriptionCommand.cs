namespace TaxVision.Subscription.Application.Subscriptions.Commands.Renew;

/// <summary>Renovación manual disparada por un admin, mientras no exista integración con
/// Billing. Marca la renovación como pagada directamente — no pasa por el intent
/// SubscriptionRenewalDue.</summary>
public sealed record RenewTenantSubscriptionCommand(Guid TenantId, Guid RequestedByUserId);
