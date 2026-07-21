namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// La solicitud transicionó de Draft a Ready porque el documento original quedó
/// disponible en CloudStorage (scan Clean) y se computó el hash pre-firma. A partir
/// de este punto el staff puede enviarla a los firmantes.
/// </summary>
public sealed record SignatureRequestReadyForSendingIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid OriginalFileId { get; init; }
    public required string DocumentHashPre { get; init; }
}
