namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Publicado por Auth al solicitar recuperación de contraseña. Notification envía el email.</summary>
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string RawToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }

    // Decide el destino del link: CustomerPortal → portal, staff → CRM. Opcional para que un
    // mensaje viejo en vuelo durante el deploy siga deserializando (cae a StaffBase, el de antes).
    public string? ActorType { get; init; }
}
