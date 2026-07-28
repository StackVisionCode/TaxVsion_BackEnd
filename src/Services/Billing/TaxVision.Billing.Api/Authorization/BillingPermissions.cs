namespace TaxVision.Billing.Api.Authorization;

/// <summary>Permisos humanos aceptados por los endpoints de Billing. Ya existen y están seeded en
/// Auth (PermissionCatalog.cs: billing.view / billing.manage) — no requiere migración de Auth nueva.
/// El policy provider que los aplica (patrón GrowthAuthorizationPolicyProvider) se agrega en B2.</summary>
public static class BillingPermissions
{
    public const string View = "billing.view";
    public const string Manage = "billing.manage";
}
