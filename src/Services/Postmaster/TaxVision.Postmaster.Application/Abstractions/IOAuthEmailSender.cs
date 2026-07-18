using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Hermano de <see cref="IEmailSender"/> — envía vía la cuenta OAuth ya resuelta por
/// <see cref="TaxVision.Postmaster.Application.Providers.IOAuthProviderResolver"/>. El token nunca
/// llega acá (D3 §2.1 punto 3) — la implementación (<c>ConnectorsSendClient</c>) llama a Connectors
/// por M2M, que es quien de verdad habla con Gmail/Graph.
/// </summary>
public interface IOAuthEmailSender
{
    /// <summary>
    /// <paramref name="inReplyToInternetMessageId"/>/<paramref name="references"/>/<paramref name="replyToProviderMessageId"/>
    /// forman el bloque de threading (D3 §6) — todos opcionales, null si el envío no es un reply.
    /// <paramref name="attachments"/> ya viene descargado por <see cref="IOutboundAttachmentFetcher"/>
    /// (D3 Compose §11.3/§Fase 4) — vacío por default para no romper el canal de notificaciones
    /// automáticas, que nunca adjunta nada.
    /// </summary>
    Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedOAuthProvider provider,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        CancellationToken ct
    );
}
