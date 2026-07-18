namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// <c>GET /correspondence/messages/{id}/attachments/{attachmentId}/download-url</c> (Fase 8) —
/// llamada barata y separada de <see cref="DownloadAttachmentCommand"/>: solo pide la URL
/// presignada de un attachment que YA está <see cref="Domain.Inbox.AttachmentDownloadStatus.Downloaded"/>.
/// </summary>
public sealed record GetAttachmentDownloadUrlQuery(Guid TenantId, Guid IncomingEmailId, Guid AttachmentId);
