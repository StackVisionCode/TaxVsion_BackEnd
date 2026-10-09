namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>Documentos cuyos participantes completaron todos sus campos y ya pueden sellarse.</summary>
public sealed record SignatureDocumentsReadyForSealingIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required IReadOnlyList<Guid> DocumentIds { get; init; }
    public required DateTime ReadyAtUtc { get; init; }
    public required string IdempotencyKey { get; init; }
}
