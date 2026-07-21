namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al cambiar los roles de un usuario. Los servicios que cachean
/// permisos deben invalidar entradas con versión anterior a PermissionsVersion.
/// </summary>
public sealed record UserRolesChangedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required int PermissionsVersion { get; init; }
    public string[] RoleNames { get; init; } = [];

    /// <summary>Fase 2 del plan de notificaciones dinámicas — para que un consumidor pueda
    /// correlacionar "a este usuario le afecta un cambio de permisos de este rol" cuando llegue
    /// <see cref="RolePermissionsChangedIntegrationEvent"/>, sin volver a preguntarle a Auth.</summary>
    public Guid[] RoleIds { get; init; } = [];

    /// <summary>
    /// Códigos de permiso efectivos del usuario tras el cambio (no solo nombres de rol) —
    /// para que un consumidor pueda mantener su propia proyección de "quién tiene qué
    /// permiso" sin duplicar el catálogo de Auth ni tener que resolver rol→permisos por su
    /// cuenta. Ver Fase 1 del plan de notificaciones dinámicas.
    /// </summary>
    public string[] PermissionCodes { get; init; } = [];
}
