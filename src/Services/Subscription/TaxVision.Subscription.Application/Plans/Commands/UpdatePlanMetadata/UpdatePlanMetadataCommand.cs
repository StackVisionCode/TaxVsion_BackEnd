namespace TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;

/// <summary>CAT-F2.3 — autoría: edita Name/Description del plan. No toca Code, Tier ni versiones;
/// un cambio de módulos/precios usa <c>SetPlanModules</c>/<c>SetPlanPrices</c>.</summary>
public sealed record UpdatePlanMetadataCommand(Guid PlanId, string Name, string Description, Guid ActorUserId);
