namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante falló un intento de verificación en el método indicado. Contadores y
/// eventual lockout se propagan para audit y para pintar el estado en el dashboard.
/// </summary>
public sealed record SignerVerificationFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SignerId { get; init; }
    public required string Method { get; init; }
    public required DateTime AttemptedAtUtc { get; init; }
    public required int FailedAttempts { get; init; }
    public DateTime? LockedUntilUtc { get; init; }
    public string? ClientIp { get; init; }
}
