using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>
/// Catálogo global de permisos. Los GUID son fijos y deterministas para que el
/// sembrado por migración (HasData) sea estable entre entornos.
/// </summary>
public static class PermissionCatalog
{
    // Usuarios y seguridad
    public const string UsersView = "users.view";
    public const string UsersInvite = "users.invite";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";
    public const string SettingsManage = "settings.manage";
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    public const string SubscriptionManage = "subscription.manage";

    // Módulos operativos
    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string SignaturesRequest = "signatures.request";
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";
    public const string EmailUse = "email.use";
    public const string CommsCalls = "comms.calls";
    public const string CampaignsManage = "campaigns.manage";
    public const string ReportsView = "reports.view";

    // Portal del cliente final
    public const string PortalCallsUse = "portal.calls.use";
    public const string PortalMilesUse = "portal.miles.use";
    public const string PortalFoldersView = "portal.folders.view";
    public const string PortalSignaturesSign = "portal.signatures.sign";

    public sealed record PermissionDefinition(
        Guid Id,
        string Code,
        string Module,
        string Description,
        bool IsCustomerPortal);

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(new Guid("a1000000-0000-0000-0000-000000000001"), UsersView, "users", "Ver usuarios del tenant", false),
        new(new Guid("a1000000-0000-0000-0000-000000000002"), UsersInvite, "users", "Invitar usuarios", false),
        new(new Guid("a1000000-0000-0000-0000-000000000003"), UsersManage, "users", "Activar, desactivar y editar usuarios", false),
        new(new Guid("a1000000-0000-0000-0000-000000000004"), RolesManage, "users", "Gestionar roles y permisos", false),
        new(new Guid("a1000000-0000-0000-0000-000000000005"), AuditView, "audit", "Consultar auditoría", false),
        new(new Guid("a1000000-0000-0000-0000-000000000006"), SettingsManage, "settings", "Gestionar configuración del tenant", false),
        new(new Guid("a1000000-0000-0000-0000-000000000007"), BillingView, "billing", "Ver facturación y suscripción", false),
        new(new Guid("a1000000-0000-0000-0000-000000000008"), BillingManage, "billing", "Gestionar métodos de pago y facturación", false),
        new(new Guid("a1000000-0000-0000-0000-000000000009"), SubscriptionManage, "billing", "Cambiar plan y gestionar suscripción", false),
        new(new Guid("a1000000-0000-0000-0000-000000000010"), CustomersView, "customers", "Ver clientes", false),
        new(new Guid("a1000000-0000-0000-0000-000000000011"), CustomersManage, "customers", "Crear y editar clientes", false),
        new(new Guid("a1000000-0000-0000-0000-000000000012"), SignaturesRequest, "signatures", "Solicitar firmas", false),
        new(new Guid("a1000000-0000-0000-0000-000000000013"), DocumentsView, "documents", "Ver documentos", false),
        new(new Guid("a1000000-0000-0000-0000-000000000014"), DocumentsManage, "documents", "Gestionar documentos", false),
        new(new Guid("a1000000-0000-0000-0000-000000000015"), EmailUse, "email", "Usar el módulo de correo", false),
        new(new Guid("a1000000-0000-0000-0000-000000000016"), CommsCalls, "comms", "Realizar llamadas y meetings", false),
        new(new Guid("a1000000-0000-0000-0000-000000000017"), CampaignsManage, "campaigns", "Gestionar campañas", false),
        new(new Guid("a1000000-0000-0000-0000-000000000018"), ReportsView, "reports", "Ver dashboard y reportes", false),
        new(new Guid("a1000000-0000-0000-0000-000000000019"), PortalCallsUse, "portal", "El cliente puede realizar llamadas", true),
        new(new Guid("a1000000-0000-0000-0000-000000000020"), PortalMilesUse, "portal", "El cliente puede usar el módulo de millas", true),
        new(new Guid("a1000000-0000-0000-0000-000000000021"), PortalFoldersView, "portal", "El cliente puede ver folders de su perfil", true),
        new(new Guid("a1000000-0000-0000-0000-000000000022"), PortalSignaturesSign, "portal", "El cliente puede firmar documentos", true)
    ];

    private static readonly Dictionary<string, Guid> IdsByCode =
        All.ToDictionary(definition => definition.Code, definition => definition.Id);

    public static Guid IdOf(string code) => IdsByCode[code];

    /// <summary>Permisos por defecto de cada rol de sistema.</summary>
    public static IReadOnlyCollection<string> SystemRoleDefaults(string systemRoleName) =>
        systemRoleName switch
        {
            Role.SystemTenantAdmin =>
                All.Where(definition => !definition.IsCustomerPortal)
                    .Select(definition => definition.Code)
                    .ToArray(),
            Role.SystemEmployee =>
            [
                CustomersView, CustomersManage, SignaturesRequest,
                DocumentsView, DocumentsManage, EmailUse, CommsCalls, ReportsView
            ],
            Role.SystemCustomerPortal =>
            [
                PortalFoldersView, PortalSignaturesSign
            ],
            _ => []
        };

    /// <summary>
    /// Permisos efectivos de respaldo cuando un usuario aún no tiene roles asignados
    /// (usuarios creados antes del modelo RBAC).
    /// </summary>
    public static IReadOnlyCollection<string> DefaultsFor(UserActorType actorType) =>
        actorType switch
        {
            UserActorType.TenantAdmin or UserActorType.PlatformAdmin =>
                SystemRoleDefaults(Role.SystemTenantAdmin),
            UserActorType.TenantEmployee => SystemRoleDefaults(Role.SystemEmployee),
            UserActorType.CustomerPortal => SystemRoleDefaults(Role.SystemCustomerPortal),
            _ => []
        };
}
