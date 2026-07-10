namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante aceptó el consent/disclosure form. Precondición implícita antes de
/// aceptar la firma electrónica. Se emite para audit y para actualizar el estado en
/// el dashboard del staff.
/// </summary>
public sealed record SignerConsentAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime AcceptedAtUtc { get; init; }
    public string? ClientIp { get; init; }
}
