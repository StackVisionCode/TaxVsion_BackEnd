namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Descarga bajo demanda de un attachment (Fase 8) — <c>POST /correspondence/messages/{id}/attachments/{attachmentId}/download</c>.
/// <see cref="ActorId"/> viene del JWT (<c>sub</c>), nunca del cuerpo/query: alimenta
/// <c>StorageAccessLog.ActorId</c> del lado de CloudStorage vía <c>SaveFileRequestedIntegrationEvent</c>.
/// </summary>
public sealed record DownloadAttachmentCommand(Guid TenantId, Guid IncomingEmailId, Guid AttachmentId, Guid ActorId);
