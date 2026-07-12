namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante superó el reto de Practitioner PIN. Precondición para que el firmante
/// pueda ejecutar <c>/sign</c>. Se emite para audit y para actualizar el estado en el
/// dashboard staff.
/// </summary>
public sealed record SignerPinVerifiedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime VerifiedAtUtc { get; init; }
    public string? ClientIp { get; init; }
}
