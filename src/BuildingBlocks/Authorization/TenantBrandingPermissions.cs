namespace BuildingBlocks.Authorization;

public static class TenantBrandingPermissions
{
    /// <summary>El TenantAdmin gestiona la marca de SU propio tenant (colores/logo/favicon).</summary>
    public const string Manage = "branding.manage";

    /// <summary>Gestiona la marca del SISTEMA (defaults de la plataforma por superficie). Reservado
    /// al PlatformAdmin: es <c>PlatformOnly</c> en el catálogo, así que el rol de sistema
    /// "Tenant Admin" nunca lo recibe. Un TenantAdmin recibe 403 en los endpoints que lo exigen.</summary>
    public const string Platform = "platform.branding.manage";
}
