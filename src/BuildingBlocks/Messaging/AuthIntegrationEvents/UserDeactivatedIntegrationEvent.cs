namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al desactivar un usuario. Libera el asiento del plan.</summary>
public sealed record UserDeactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
}
