namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Fase 12 — <c>POST /correspondence/drafts/{id}/attachments</c>: el archivo YA fue subido a
/// CloudStorage por el frontend (flujo de upload existente, staged) — este comando solo agrega
/// una referencia al draft, nunca bytes.
/// </summary>
public sealed record AttachFileToDraftCommand(
    Guid TenantId,
    Guid DraftId,
    Guid FileId,
    string Filename,
    string ContentType,
    long SizeBytes
);
