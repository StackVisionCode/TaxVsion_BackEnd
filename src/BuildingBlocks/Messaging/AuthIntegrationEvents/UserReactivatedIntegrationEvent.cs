using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al reactivar un usuario previamente desactivado. Contraparte de
/// <see cref="UserDeactivatedIntegrationEvent"/> — sin este evento, cualquier proyeccion local
/// de "empleados activos" (ej. TenantEmployeeDirectory en Customer) queda desactualizada para
/// siempre tras una reactivacion.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.UserReactivatedIntegrationEvent")]
public sealed record UserReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
}
