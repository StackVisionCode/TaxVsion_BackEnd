namespace TaxVision.Correspondence.Api.Requests;

/// <summary>
/// <c>POST /correspondence/drafts/{id}/attachments</c> (Fase 12) — el archivo ya fue subido a
/// CloudStorage por el frontend con su propio flujo de upload; acá solo viaja la referencia.
/// </summary>
public sealed record AttachFileToDraftBody(Guid FileId, string Filename, string ContentType, long SizeBytes);
