namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando cambian los permisos de un ROL (no de un usuario puntual — ver
/// <see cref="UserRolesChangedIntegrationEvent"/> para eso). Fase 2 del plan de notificaciones
/// dinámicas: sin este evento, editar los permisos de un rol con 50 usuarios asignados no
/// propaga a ninguna proyección local, y quedan con datos viejos hasta que cada uno de esos 50
/// reciba individualmente un cambio de rol en el futuro (que puede no pasar nunca).
///
/// <see cref="PermissionCodes"/> es el set COMPLETO de permisos del rol post-cambio, no un
/// diff. El "quién tiene este rol" queda del lado del consumidor: cada proyección local que
/// guarda <c>RoleIds</c> por usuario (ver <see cref="UserRolesChangedIntegrationEvent.RoleIds"/>)
/// puede resolver "a quién le pega este cambio" sin llamar a Auth.
/// </summary>
public sealed record RolePermissionsChangedIntegrationEvent : IntegrationEvent
{
    public required Guid RoleId { get; init; }
    public required string RoleName { get; init; }
    public string[] PermissionCodes { get; init; } = [];

    /// <summary>Versión del rol (no del usuario ni del tenant) — sube en cada
    /// <c>Role.SetPermissions</c>. Un consumidor la usa para descartar un evento entregado
    /// fuera de orden (versión menor a la que ya tiene cacheada para este RoleId).</summary>
    public required int PermissionsVersion { get; init; }
}
