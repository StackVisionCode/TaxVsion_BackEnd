namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Descarga bajo demanda de un attachment (Fase 8) — <c>POST /correspondence/messages/{id}/attachments/{attachmentId}/download</c>.
/// <see cref="ActorId"/> viene del JWT (<c>sub</c>), nunca del cuerpo/query: alimenta
/// <c>StorageAccessLog.ActorId</c> del lado de CloudStorage vía <c>SaveFileRequestedIntegrationEvent</c>.
/// </summary>
/// <param name="VisibleAccountIds">
/// A1 — gate de buzón, el mismo que ya aplicaban el cuerpo y el listado de adjuntos del mensaje. Sin
/// esto, un empleado sin acceso al buzón de oficina podía descargar el adjunto de un correo que ni
/// siquiera puede abrir: la lista se lo ocultaba, pero el id del adjunto bastaba. <c>null</c> = ve todo.
/// </param>
public sealed record DownloadAttachmentCommand(
    Guid TenantId,
    Guid IncomingEmailId,
    Guid AttachmentId,
    Guid ActorId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null
);
