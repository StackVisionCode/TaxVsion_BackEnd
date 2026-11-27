namespace BuildingBlocks.Authorization;

public static class CustomersPermissions
{
    public const string View = "customers.view";
    public const string Manage = "customers.manage";

    /// <summary>
    /// Ver TODOS los clientes del tenant, no solo los asignados (governance). Con el modelo de acceso por
    /// asignación, <see cref="View"/> pasa a significar "ver los asignados"; este permiso levanta esa
    /// restricción. TenantAdmin lo trae por defecto; puede otorgarse a un supervisor sin volverlo admin.
    /// La restricción por asignación solo se aplica con el feature-flag de visibilidad encendido.
    /// </summary>
    public const string ViewAll = "customers.view_all";

    /// <summary>
    /// Revela el SSN/ITIN/EIN en claro de un customer. Separado de <see cref="Manage"/> a
    /// propósito — editar un fiscal profile no implica poder ver el identificador completo,
    /// y viceversa. TenantAdmin/PlatformAdmin siempre pasan (ver ClaimsPrincipalExtensions.HasPermission),
    /// el resto necesita este permiso otorgado explícitamente.
    /// </summary>
    public const string FiscalProfileReveal = "customers.fiscalprofile.reveal";

    /// <summary>
    /// Asignar/reasignar el preparador responsable de un customer. Separado de
    /// <see cref="Manage"/> por la misma razón que <see cref="FiscalProfileReveal"/> —
    /// un TenantAdmin puede delegarlo puntualmente sin dar acceso de edición completo.
    /// </summary>
    public const string PreparerManage = "customers.preparer.manage";

    /// <summary>Importar customers en bloque (CSV/Excel). Operación administrativa — antes vivía bajo
    /// <c>[Authorize(Roles="TenantAdmin")]</c>; ahora es un permiso propio, admin-only por defecto.</summary>
    public const string Import = "customers.import";

    /// <summary>Ver la ubicación aproximada (por IP) de las sesiones del portal de un cliente. Solo admin por defecto.</summary>
    public const string PortalSessionsLocationView = "customers.portal_sessions.location.view";

    /// <summary>Enviar o cancelar el link de restablecimiento del portal de un cliente. Solo el admin raíz.</summary>
    public const string PortalCredentialsSendReset = "customers.portal_credentials.send_reset";

    /// <summary>Exigir al cliente que cambie su contraseña del portal en el próximo ingreso. Solo el admin raíz.</summary>
    public const string PortalCredentialsRequireChange = "customers.portal_credentials.require_change";

    /// <summary>Asegurar la cuenta del portal (cerrar sesiones y forzar el restablecimiento). Solo el admin raíz.</summary>
    public const string PortalCredentialsSecure = "customers.portal_credentials.secure";

    /// <summary>Resetear la verificación en dos pasos del portal de un cliente (D-10). Solo el admin raíz.</summary>
    public const string PortalMfaReset = "customers.portal_mfa.reset";
}
