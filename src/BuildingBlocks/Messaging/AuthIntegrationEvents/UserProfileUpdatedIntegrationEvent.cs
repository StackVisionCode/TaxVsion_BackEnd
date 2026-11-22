using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando cambia el perfil de un usuario: nombre/apellido, idioma o zona
/// (UpdateMyProfileCommand, SetMyLanguageCommand) o foto (confirmada o quitada). Lleva siempre el estado actual completo.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.UserProfileUpdatedIntegrationEvent")]
public sealed record UserProfileUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string LastName { get; init; }

    /// <summary>Foto actual (servida en <c>/storage/avatars/{id}</c>); null = sin foto. Opcional: aditivo.</summary>
    public Guid? AvatarFileId { get; init; }

    /// <summary>Idioma de los avisos ("en" o "es"); null = sin preferencia. Opcional: aditivo.</summary>
    public string? Language { get; init; }

    /// <summary>Zona IANA propia del usuario; null = usa la de la oficina. Opcional: aditivo.</summary>
    public string? TimeZoneId { get; init; }
}
