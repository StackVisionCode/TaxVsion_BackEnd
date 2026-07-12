namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Un firmante rechazó firmar. La solicitud completa transiciona a <c>Rejected</c> y
/// los tokens vigentes quedan invalidados por incremento de RevocationEpoch.
/// </summary>
public sealed record SignerRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime RejectedAtUtc { get; init; }
    public required int RevocationEpoch { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<Guid> PendingSignerIds { get; init; }
}
