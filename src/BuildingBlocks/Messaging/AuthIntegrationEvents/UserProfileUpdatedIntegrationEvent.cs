namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth cuando un usuario actualiza su nombre/apellido (UpdateMyProfileCommand).</summary>
public sealed record UserProfileUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string LastName { get; init; }
}
