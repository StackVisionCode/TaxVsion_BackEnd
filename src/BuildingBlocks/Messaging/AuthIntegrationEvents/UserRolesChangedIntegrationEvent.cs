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
}
