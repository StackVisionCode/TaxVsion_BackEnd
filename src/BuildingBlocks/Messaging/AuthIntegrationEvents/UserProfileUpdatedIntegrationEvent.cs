namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando cambia el perfil visible de un usuario: nombre/apellido
/// (UpdateMyProfileCommand) o foto (confirmada o quitada). Lleva siempre el estado actual completo.
/// </summary>
public sealed record UserProfileUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string LastName { get; init; }

    /// <summary>Foto actual (servida en <c>/storage/avatars/{id}</c>); null = sin foto. Opcional: aditivo.</summary>
    public Guid? AvatarFileId { get; init; }
}
