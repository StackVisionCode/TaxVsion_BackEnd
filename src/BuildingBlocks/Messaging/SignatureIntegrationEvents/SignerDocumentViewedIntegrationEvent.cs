namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// F5 — El firmante vio el DOCUMENTO original (no solo el enlace). Semántica distinta a
/// <see cref="SignerConsentAcceptedIntegrationEvent"/> y a FirstViewedAtUtc: éste se emite al
/// servir los bytes del PDF por el endpoint público. El audit lo appende con
/// <c>SignatureAuditEventKind.DocumentViewed</c>; las actas lo pintan como línea aparte.
/// </summary>
public sealed record SignerDocumentViewedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid SignerId { get; init; }
    public required Guid DocumentId { get; init; }
    public required DateTime ViewedAtUtc { get; init; }
    public string? ClientIp { get; init; }
}
