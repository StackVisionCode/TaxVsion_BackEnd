using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Hermano de <see cref="IEmailSender"/> — envía vía el buzón conectado ya resuelto por
/// <see cref="TaxVision.Postmaster.Application.Providers.IConnectedMailboxResolver"/>. El token nunca
/// llega acá (D3 §2.1 punto 3) — la implementación (<c>ConnectorsSendClient</c>) llama a Connectors
/// por M2M, que es quien de verdad habla con Gmail/Graph.
/// </summary>
public interface IConnectedMailboxSender
{
    /// <summary>
    /// <paramref name="inReplyToInternetMessageId"/>/<paramref name="references"/>/<paramref name="replyToProviderMessageId"/>
    /// forman el bloque de threading (D3 §6) — todos opcionales, null si el envío no es un reply.
    /// <paramref name="attachments"/> ya viene descargado por <see cref="IOutboundAttachmentFetcher"/>
    /// (D3 Compose §11.3/§Fase 4). <paramref name="inlineAssets"/> son las imágenes <c>cid:</c> del
    /// cuerpo, por <see cref="IInlineAssetFetcher"/>. Sin ellas el logo llega roto.
    /// </summary>
    Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedMailbox provider,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        CancellationToken ct
    );
}
