namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Respuesta de <see cref="DownloadAttachmentHandler"/>. Deliberadamente NO incluye una URL de
/// descarga — eso es una llamada separada y barata (<c>GetAttachmentDownloadUrlHandler</c>) por
/// diseño (plan §22): el trigger de descarga y la obtención de la URL firmada son dos endpoints
/// distintos.
/// </summary>
public sealed record DownloadAttachmentResult(Guid AttachmentId, string DownloadStatus, Guid CloudStorageFileId);
