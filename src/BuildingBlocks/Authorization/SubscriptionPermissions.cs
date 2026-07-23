namespace BuildingBlocks.Authorization;

public static class SubscriptionPermissions
{
    // Reusa el código ya sembrado en el catálogo global de Auth (PermissionCatalog.AuditView,
    // GUID a1000000-0000-0000-0000-000000000005) — Subscription no referencia
    // TaxVision.Auth.Domain (otro microservicio), así que necesita su propia constante local
    // con el mismo string exacto para que [HasPermission] compare contra la misma fila.
    public const string AuditView = "audit.view";
    public const string PlanChange = "subscription.plan.change";
    public const string Suspend = "subscription.suspend";
    public const string Reactivate = "subscription.reactivate";
    public const string Renew = "subscription.renew";
    public const string AdminCrossTenant = "subscription.admin.cross_tenant";
    public const string SeatsManage = "seats.manage";
    public const string AddOnsManage = "addons.manage";
}
