using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// F9 — Hecho interno emitido cuando el preparador reemplaza el PDF original de un documento del
/// borrador. Si <c>FieldsInvalidated &gt; 0</c>, el nuevo PDF tenía distinto número de páginas y las
/// coordenadas normalizadas dejaron de ser válidas, por eso se borraron los campos de ese doc.
/// </summary>
public sealed record DocumentReplaced(
    Guid DocumentId,
    Guid OldFileId,
    Guid NewFileId,
    int? OldPageCount,
    int? NewPageCount,
    int FieldsInvalidated,
    DateTime OccurredAtUtc
) : IDomainEvent;
