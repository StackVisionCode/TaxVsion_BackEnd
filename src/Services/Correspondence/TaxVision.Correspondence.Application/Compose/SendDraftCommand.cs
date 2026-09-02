namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// <see cref="ActorId"/> viene del JWT (<c>sub</c>), nunca del cuerpo/query — mismo criterio que
/// <c>DownloadAttachmentCommand.ActorId</c>. Alimenta <c>CorrespondenceAuditLog.UserId</c>.
/// <para>
/// <see cref="CorrelationId"/> se pasa DESDE el controller (scope HTTP, donde
/// <c>CorrelationIdMiddleware</c> ya la puso): al ejecutarse el handler vía <c>bus.InvokeAsync</c>,
/// el <c>ICorrelationContext</c> del scope de Wolverine viene vacío, y sin ella el
/// <c>CorrespondenceAuditLog</c> se descartaba por validación (correlationId requerido).
/// </para>
/// </summary>
public sealed record SendDraftCommand(Guid TenantId, Guid DraftId, Guid ActorId, string CorrelationId);
