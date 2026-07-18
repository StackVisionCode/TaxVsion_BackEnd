namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Fila de metadata para <c>GET /correspondence/messages/{id}/attachments</c> (Fase 7). Nunca
/// carga el binario ni <c>FailureReason</c> — el plan de diseño §21 lista exactamente estos
/// campos, cero bytes en el response.
/// </summary>
public sealed record AttachmentSummary(
    Guid AttachmentId,
    string Filename,
    string ContentType,
    long SizeBytes,
    bool IsInline,
    string DownloadStatus,
    Guid? CloudStorageFileId
);
