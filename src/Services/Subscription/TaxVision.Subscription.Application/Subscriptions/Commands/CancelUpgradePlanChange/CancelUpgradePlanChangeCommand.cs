namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelUpgradePlanChange;

/// <summary>Cancela el upgrade AwaitingPayment del tenant (único por tenant, por el guard de doble cobro).</summary>
public sealed record CancelUpgradePlanChangeCommand(Guid TenantId, Guid ActorUserId, string? Reason);
