using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <summary>
/// Envío directo iniciado por un preparador desde Correspondence (D3 Compose §14/§16 Fase 5) — a
/// diferencia de <c>NotificationsEmailSendRequestedIntegrationEvent</c>, siempre vía cuenta OAuth/manual
/// elegida explícitamente (<see cref="AccountId"/>), nunca el canal automático del sistema.
/// </summary>
public sealed record SendCorrespondenceMessageCommand(
    Guid TenantId,
    Guid CorrespondenceDraftId,
    Guid AccountId,
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    IReadOnlyList<OutboundAttachmentRef> Attachments,
    string? InReplyToInternetMessageId,
    IReadOnlyList<string>? References,
    string? ReplyToProviderMessageId
);
