namespace TaxVision.Correspondence.Application.Compose;

/// <summary><c>DELETE /correspondence/drafts/{id}/attachments/{fileId}</c> (Fase 12).</summary>
public sealed record RemoveDraftAttachmentCommand(Guid TenantId, Guid DraftId, Guid FileId);
