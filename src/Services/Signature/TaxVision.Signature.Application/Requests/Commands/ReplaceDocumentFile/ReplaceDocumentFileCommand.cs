namespace TaxVision.Signature.Application.Requests.Commands.ReplaceDocumentFile;

/// <summary>
/// F9 — Reemplaza el PDF original de un documento del borrador. Si el nuevo PDF tiene distinto
/// número de páginas, el aggregate invalida los campos de ese documento y el response reporta
/// cuántos cayeron (<c>FieldsInvalidated</c>) para que la UI pueda avisar al preparador.
/// </summary>
public sealed record ReplaceDocumentFileCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid DocumentId,
    Guid NewFileId,
    int? NewPageCount
);
