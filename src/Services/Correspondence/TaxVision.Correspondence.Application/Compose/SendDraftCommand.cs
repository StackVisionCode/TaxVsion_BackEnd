namespace TaxVision.Correspondence.Application.Compose;

/// <summary><see cref="ActorId"/> viene del JWT (<c>sub</c>), nunca del cuerpo/query — mismo criterio que <c>DownloadAttachmentCommand.ActorId</c>. Alimenta <c>CorrespondenceAuditLog.UserId</c>.</summary>
public sealed record SendDraftCommand(Guid TenantId, Guid DraftId, Guid ActorId);
