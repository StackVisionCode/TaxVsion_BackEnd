namespace TaxVision.Subscription.Application.Plans.Commands.CreatePlan;

/// <summary>CAT-F2.3 — autoría: crea un plan nuevo con una versión v1 draft inicial sin módulos ni
/// entitlements ni precios. El autor completa la versión en F2.4 y la publica en F2.5.
/// <paramref name="Tier"/> y <paramref name="SupportedBillingCycles"/> llegan como strings parseables
/// a los enums <c>PlanTier</c> y <c>BillingCycle</c>.</summary>
public sealed record CreatePlanCommand(
    string Code,
    string Name,
    string Description,
    string Tier,
    IReadOnlyList<string> SupportedBillingCycles,
    int TrialDaysDefault,
    Guid ActorUserId
);

public sealed record CreatePlanResult(Guid PlanId, Guid VersionId, int VersionNumber);
