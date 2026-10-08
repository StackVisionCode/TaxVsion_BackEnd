namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>Todos los documentos de la solicitud terminaron de sellarse.</summary>
public sealed record SignatureRequestSealingCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required int DocumentCount { get; init; }
    public required DateTime SealedAtUtc { get; init; }
}
