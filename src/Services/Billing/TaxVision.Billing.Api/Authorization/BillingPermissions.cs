namespace TaxVision.Billing.Api.Authorization;

/// <summary>Permisos humanos aceptados por los endpoints de Billing. Ya están seeded en Auth
/// (PermissionCatalog.cs: billing.view / billing.manage), y el rol de sistema TenantAdmin los recibe
/// por SystemTenantAdminRootPermissions() — no hace falta migración nueva ni asignación manual.</summary>
public static class BillingPermissions
{
    public const string View = "billing.view";
    public const string Manage = "billing.manage";
}
