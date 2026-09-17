namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;

/// <summary>
/// Recálculo masivo de la flota: encola un <c>RecalculateEntitlementsForPlanCommand</c> por cada plan
/// publicado (que a su vez abanica un <c>RecalculateEntitlementsCommand</c> por tenant). Herramienta de
/// backfill idempotente y determinista para reconciliar a TODOS los tenants existentes tras un cambio de
/// forma de los entitlements (p.ej. el cupo efectivo de staff, <c>MaxStaffUsers</c>). No afecta a los
/// tenants nuevos: esos recalculan solos al crearse (TenantCreated/ActivateFromOnboarding), así que
/// republicar su snapshot solo recomputa el mismo valor. Solo PlatformAdmin.
/// </summary>
public sealed record RecalculateEntitlementsForAllPlansCommand;
