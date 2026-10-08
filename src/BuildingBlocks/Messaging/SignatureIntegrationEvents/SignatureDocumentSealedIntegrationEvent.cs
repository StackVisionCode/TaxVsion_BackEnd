namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>Un documento concreto de la solicitud terminó su pipeline de sellado.</summary>
public sealed record SignatureDocumentSealedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required Guid DocumentId { get; init; }
    public required Guid SealedFileId { get; init; }
    public required string DocumentHashPost { get; init; }
    public Guid? CertificateFileId { get; init; }
    public required DateTime SealedAtUtc { get; init; }
}
