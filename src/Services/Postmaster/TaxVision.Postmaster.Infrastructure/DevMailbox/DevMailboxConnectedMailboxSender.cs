using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.DevMailbox;

// Decorador del envío por buzón conectado (Gmail/Graph vía Connectors): copia al buzón dev y delega igual.
public sealed class DevMailboxConnectedMailboxSender(IConnectedMailboxSender inner, DevMailboxWriter writer)
    : IConnectedMailboxSender
{
    public Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedMailbox provider,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        CancellationToken ct
    )
    {
        writer.Write(message, content, provider.FromAddress, provider.FromDisplayName ?? message.FromDisplayName);
        return inner.SendAsync(
            message,
            content,
            provider,
            inReplyToInternetMessageId,
            references,
            replyToProviderMessageId,
            attachments,
            inlineAssets,
            ct
        );
    }
}
