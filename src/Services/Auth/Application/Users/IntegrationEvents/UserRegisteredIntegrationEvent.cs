using BuildingBlocks.Messaging;

namespace TaxVision.Auth.Application.Users.IntegrationEvents;

public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
    public Guid? CustomerId { get; init; }

    // Opcionales para no romper consumers existentes que ya deserializan este
    // evento (p.ej. Communication.UserPermissionsProjection). Se agregaron para
    // que Communication pueda hidratar un directorio de nombres (displayName)
    // sin depender de un round-trip HTTP a Auth.
    public string? Name { get; init; }
    public string? LastName { get; init; }
}
