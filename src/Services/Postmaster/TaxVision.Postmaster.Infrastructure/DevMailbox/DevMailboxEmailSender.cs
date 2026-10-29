using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.DevMailbox;

// Decorador del envío por proveedor de sistema (SMTP): copia al buzón dev y delega igual.
public sealed class DevMailboxEmailSender(IEmailSender inner, DevMailboxWriter writer) : IEmailSender
{
    public Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedEmailProvider provider,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        CancellationToken ct
    )
    {
        writer.Write(message, content, provider.FromAddress, provider.FromDisplayName ?? message.FromDisplayName);
        return inner.SendAsync(message, content, provider, inlineAssets, attachments, ct);
    }
}
