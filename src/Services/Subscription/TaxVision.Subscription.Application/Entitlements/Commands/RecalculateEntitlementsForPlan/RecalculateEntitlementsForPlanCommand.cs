namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;

/// <summary>
/// Recálculo masivo: dispara el recálculo de entitlements de todos los tenants de un plan. Cross-tenant
/// a propósito — sin propiedad TenantId, así el middleware no sella un tenant.
/// </summary>
public sealed record RecalculateEntitlementsForPlanCommand(Guid PlanId, int BatchSize = 200);
