namespace TaxVision.Auth.Domain.Tenants;

/// <summary>
/// Nivel de plan del tenant, para el guardarraíl anti-escalada de permisos
/// (<see cref="TaxVision.Auth.Application.Common.RolePermissionGuard"/>). Auth no depende del
/// microservicio Subscription; este enum espeja localmente el orden de <c>PlanCatalog</c>
/// (Subscription.Domain.Plans) — starter/pro/enterprise — sin acoplar los bounded contexts.
/// </summary>
public enum PlanTier
{
    Starter = 0,
    Pro = 1,
    Enterprise = 2,
}

public static class PlanTierResolver
{
    /// <summary>
    /// Resuelve el <see cref="PlanTier"/> a partir del <c>PlanCode</c> proyectado desde
    /// Subscription (<see cref="TaxVision.Auth.Domain.Tenants.TenantPlanLimits.PlanCode"/>).
    /// Códigos desconocidos o ausentes (aún no llegó la proyección del plan) resuelven al
    /// tier más restrictivo — a diferencia de <c>PlanGuard</c> (que no bloquea sin proyección
    /// porque es una validación de UX de cupos), este es un guardarraíl de seguridad y debe
    /// fallar cerrado.
    /// </summary>
    public static PlanTier FromPlanCode(string? planCode) =>
        planCode?.Trim().ToLowerInvariant() switch
        {
            "starter" => PlanTier.Starter,
            "pro" => PlanTier.Pro,
            "enterprise" => PlanTier.Enterprise,
            _ => PlanTier.Starter,
        };
}
