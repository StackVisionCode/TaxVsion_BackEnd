namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// F9 — Resultado de reemplazar el PDF de un <see cref="RequestDocument"/>. Lo devuelve el aggregate
/// para que el handler y la UI puedan avisar cuántos campos quedaron invalidados por un cambio de
/// número de páginas, sin que el caller tenga que mirar las colecciones a mano.
/// </summary>
public sealed record DocumentReplaceOutcome(
    Guid OldFileId,
    Guid NewFileId,
    int? OldPageCount,
    int? NewPageCount,
    int FieldsInvalidated
);
