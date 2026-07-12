namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al solicitar un cambio de email. Notification envía el enlace
/// de confirmación a la dirección NUEVA y un aviso a la dirección anterior.
/// </summary>
public sealed record EmailChangeRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string CurrentEmail { get; init; }
    public required string NewEmail { get; init; }
    public required string RawToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
