namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante falló un intento de PIN. Incluye el contador acumulado y — si aplica —
/// el timestamp del bloqueo automático. Notification lo consume para alertar al staff
/// cuando la cuenta se acerca al lock y para pintar el estado en el dashboard.
/// </summary>
public sealed record SignerPinFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime AttemptedAtUtc { get; init; }
    public required int FailedAttempts { get; init; }

    /// <summary><c>null</c> si el firmante aún no llegó al máximo; el timestamp de expiración del lock en caso contrario.</summary>
    public DateTime? LockedUntilUtc { get; init; }

    public string? ClientIp { get; init; }
}
