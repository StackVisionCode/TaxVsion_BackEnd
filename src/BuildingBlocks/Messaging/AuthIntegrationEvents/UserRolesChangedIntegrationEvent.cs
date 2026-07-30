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

    /// <summary>
    /// El campo nunca existió acá desde que Communication empezó a consumir este evento para
    /// mantener su <c>UserPermissionsProjection</c> — el consumer (<c>auth-consumers.ts</c>)
    /// siempre buscó <c>actorType</c>/<c>ActorType</c> en el payload y, al no encontrarlo,
    /// caía en un fallback hardcodeado a <c>"TenantEmployee"</c>. Cualquier TenantAdmin al que
    /// se le reasignaran roles quedaba con su actor type corrompido en la proyección — bug real
    /// encontrado auditando una proyección con TenantAdmin marcados como TenantEmployee.
    /// Requerido (no opcional) para que no vuelva a pasar por descuido en un publisher nuevo.
    /// </summary>
    public required string ActorType { get; init; }
}
