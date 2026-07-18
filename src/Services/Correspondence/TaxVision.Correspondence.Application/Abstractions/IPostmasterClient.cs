using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Cliente M2M hacia <c>POST /postmaster/correspondence-messages</c> (policy <c>ServiceOnly</c>,
/// D3 Compose §14/§15, ya en producción del lado de Postmaster) — el cierre síncrono de la cadena
/// Correspondence → Postmaster → Connectors → proveedor real: el usuario que aprieta "Enviar" está
/// esperando el resultado real en la MISMA request HTTP, no un evento ni un callback (plan §0/§14).
///
/// <para>
/// El split To/Cc/Bcc ya llega armado como tres listas de <see cref="string"/> — mismo shape que
/// <c>SendCorrespondenceMessageRequest.To/Cc/Bcc</c> del lado de Postmaster. La responsabilidad de
/// proyectar <see cref="Draft.Recipients"/> (que sí distingue por <c>EmailRecipientType</c>) a esas
/// tres listas vive en <c>SendDraftHandler</c>, no acá: este cliente es una capa de traducción "a
/// formato de wire", no "conoce la forma interna de un Draft" — mantenerlo así lo deja testeable
/// con listas planas, sin necesitar referenciar <c>DraftRecipient</c>/<c>EmailRecipientType</c> en
/// sus tests. <see cref="DraftAttachmentRef"/> y <see cref="ReplyContext"/> en cambio se reusan tal
/// cual del dominio: ya tienen exactamente los campos que Postmaster espera
/// (<c>CorrespondenceAttachmentRequest</c>/<c>CorrespondenceReplyContextRequest</c>), duplicarlos
/// acá en un DTO paralelo sería puro ceremonial sin beneficio (DRY).
/// </para>
/// </summary>
public interface IPostmasterClient
{
    Task<Result<SendDraftPostmasterResult>> SendAsync(
        Guid tenantId,
        Guid draftId,
        Guid accountId,
        string subject,
        string html,
        string? text,
        IReadOnlyList<string> to,
        IReadOnlyList<string> cc,
        IReadOnlyList<string> bcc,
        IReadOnlyList<DraftAttachmentRef> attachments,
        ReplyContext? replyContext,
        CancellationToken ct = default
    );
}

/// <summary>Mirror 1:1 de <c>SendCorrespondenceMessageResult</c> (Postmaster) — <c>ProviderMessageId</c> es null en el replay de idempotencia, igual que del lado de Postmaster.</summary>
public sealed record SendDraftPostmasterResult(Guid SentMessageId, string? ProviderMessageId);
