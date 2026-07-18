namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado al aceptar una invitación (nuevo usuario registrado). Movido desde el namespace
/// local de Auth (Fase 9, wiring de auth.welcome) para que Notification pueda consumirlo —
/// antes solo lo consumía Communication (Node), que lo referenciaba por CLR type name completo,
/// no por este tipo compartido.
/// </summary>
public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
    public Guid? CustomerId { get; init; }

    // Opcionales para no romper consumers existentes que ya deserializan este
    // evento (p.ej. Communication.UserPermissionsProjection).
    public string? Name { get; init; }
    public string? LastName { get; init; }
}
