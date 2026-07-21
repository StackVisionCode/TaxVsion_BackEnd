namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante completó exitosamente el reto de verificación en el método indicado.
/// Notification lo consume para actualizar el estado en el dashboard staff. Signature
/// ya considera al firmante "verified" para el guard de <c>MarkSignerSigned</c>.
/// </summary>
public sealed record SignerVerificationSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SignerId { get; init; }
    public required string Method { get; init; }
    public required DateTime VerifiedAtUtc { get; init; }
    public string? ClientIp { get; init; }
}
