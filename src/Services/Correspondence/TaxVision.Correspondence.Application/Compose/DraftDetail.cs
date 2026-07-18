using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Vista completa de un <see cref="Draft"/> para <c>GET /correspondence/drafts/{id}</c> (Fase 11)
/// — todo lo que el composer del frontend necesita para renderizar/retomar una redacción:
/// subject, bodies, recipients, attachments, replyContext, status. <see cref="ReplyContext"/> se
/// expone como el VO de dominio directo, mismo criterio que <see cref="StartReplyResult"/>
/// (Fase 10) ya estableció para este mismo tipo — no hay un DTO paralelo que duplique sus campos.
/// </summary>
public sealed record DraftDetail(
    Guid DraftId,
    Guid CustomerId,
    Guid AccountId,
    string Subject,
    string HtmlBody,
    string? TextBody,
    string Status,
    IReadOnlyList<DraftRecipientSummary> Recipients,
    IReadOnlyList<DraftAttachmentSummary> Attachments,
    ReplyContext? ReplyContext,
    Guid? SentMessageId,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? LastAutoSavedAtUtc
);
