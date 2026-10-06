using MimeKit;
using MimeKit.Utils;
using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Infrastructure.Providers;

/// <summary>
/// El MIME de salida, uno solo para Gmail y SMTP manual. Estaba duplicado byte a byte en los dos
/// clientes, y ésa es justo la forma en que el soporte de inline assets se agrega a uno y se olvida
/// en el otro. Graph no pasa por acá: arma su propio JSON, sin MIME.
/// </summary>
public static class OutboundMimeBuilder
{
    public static MimeMessage Build(string fromAddress, string? fromDisplayName, OutboundMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(fromDisplayName ?? string.Empty, fromAddress));

        var atIndex = fromAddress.IndexOf('@');
        var fromDomain = atIndex >= 0 ? fromAddress[(atIndex + 1)..] : "taxvision.local";
        mime.MessageId = MimeUtils.GenerateMessageId(fromDomain);

        foreach (var to in message.To)
            mime.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in message.Cc)
            mime.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in message.Bcc)
            mime.Bcc.Add(MailboxAddress.Parse(bcc));
        if (!string.IsNullOrWhiteSpace(message.ReplyToDisplayAddress))
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyToDisplayAddress));

        mime.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(message.InReplyToInternetMessageId))
            mime.InReplyTo = message.InReplyToInternetMessageId;
        foreach (var reference in message.References ?? [])
            mime.References.Add(reference);

        var bodyBuilder = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text };
        foreach (var attachment in message.Attachments)
            bodyBuilder.Attachments.Add(
                attachment.Filename,
                attachment.Content,
                ContentType.Parse(attachment.ContentType)
            );

        // LinkedResources, no Attachments: asi MimeKit arma multipart/related y el cid: resuelve.
        foreach (var inline in message.InlineAssets)
        {
            var entity = bodyBuilder.LinkedResources.Add(
                inline.ContentId,
                inline.Content,
                ContentType.Parse(inline.ContentType)
            );
            entity.ContentId = inline.ContentId;
            entity.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }

        mime.Body = bodyBuilder.ToMessageBody();
        return mime;
    }
}
