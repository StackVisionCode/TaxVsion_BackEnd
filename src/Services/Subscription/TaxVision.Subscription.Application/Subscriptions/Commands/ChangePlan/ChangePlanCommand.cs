namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary><paramref name="BillingCycle"/> es opcional — "Monthly"/"Yearly"/etc (string, se
/// parsea en el handler). Null = mantener el ciclo actual de la suscripción.</summary>
public sealed record ChangePlanCommand(Guid TenantId, string PlanCode, string? BillingCycle, Guid RequestedByUserId);
