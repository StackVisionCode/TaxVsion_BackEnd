namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Un firmante rechazó firmar. La solicitud completa transiciona a <c>Rejected</c> y
/// los tokens vigentes quedan invalidados por incremento de RevocationEpoch.
/// </summary>
public sealed record SignerRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime RejectedAtUtc { get; init; }
    public required int RevocationEpoch { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<Guid> PendingSignerIds { get; init; }

    /// <summary>Snapshot de contacto de los firmantes aún pendientes — se les notifica que la solicitud fue cancelada.</summary>
    public required IReadOnlyList<SignerContactSnapshot> PendingSigners { get; init; }
}
