namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al crear o reenviar una invitación. Notification lo consume
/// para enviar el email/SMS con el enlace de activación. El token viaja únicamente
/// por el bus interno y nunca se persiste en claro.
/// </summary>
public sealed record InvitationCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid InvitationId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
    public required string RawToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public string? TenantName { get; init; }
    public string? TenantSubdomain { get; init; }
    public string? InviterName { get; init; }
    public bool IsResend { get; init; }
}
