namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// F9 — El preparador reemplazó el PDF original de un documento del borrador. El audit lo appende
/// con <see cref="TaxVision.Signature.Domain.Audit.SignatureAuditEventKind.DocumentReplaced"/> y
/// deja constancia de los fileId viejo/nuevo, el page count antes/después y cuántos campos
/// quedaron invalidados por el cambio de páginas.
/// </summary>
public sealed record SignatureDocumentReplacedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid DocumentId { get; init; }
    public required Guid OldFileId { get; init; }
    public required Guid NewFileId { get; init; }
    public int? OldPageCount { get; init; }
    public int? NewPageCount { get; init; }
    public required int FieldsInvalidated { get; init; }
    public required DateTime ReplacedAtUtc { get; init; }
    public Guid? ReplacedByUserId { get; init; }
}
