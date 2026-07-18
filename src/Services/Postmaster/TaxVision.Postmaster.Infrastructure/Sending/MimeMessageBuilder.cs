using MimeKit;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Infrastructure.Sending;

/// <summary>
/// Arma el <see cref="MimeMessage"/> final a partir de un <see cref="SentMessage"/> ya resuelto.
/// Extraído de <c>SmtpEmailSender</c> (Fase 3) para que Fase 5 pueda reutilizarlo con
/// <see cref="InlineAssetBytes"/> reales (logos vía CID) sin duplicar la construcción del cuerpo.
/// Cuando hay imágenes inline, MimeKit arma automáticamente un <c>multipart/related</c>.
/// </summary>
public static class MimeMessageBuilder
{
    public static MimeMessage Build(
        SentMessage message,
        RenderedContent content,
        ResolvedEmailProvider provider,
        IReadOnlyList<InlineAssetBytes> inlineAssets
    )
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(
            new MailboxAddress(provider.FromDisplayName ?? message.FromDisplayName, provider.FromAddress)
        );
        if (message.ReplyTo is not null)
            mimeMessage.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));

        AddRecipients(mimeMessage, message);
        mimeMessage.Subject = content.Subject;
        mimeMessage.Body = BuildBody(content, inlineAssets);
        return mimeMessage;
    }

    private static MimeEntity BuildBody(RenderedContent content, IReadOnlyList<InlineAssetBytes> inlineAssets)
    {
        var bodyBuilder = new BodyBuilder { HtmlBody = content.Html, TextBody = content.Text };
        foreach (var asset in inlineAssets)
            AddLinkedResource(bodyBuilder, asset);

        return bodyBuilder.ToMessageBody();
    }

    private static void AddLinkedResource(BodyBuilder bodyBuilder, InlineAssetBytes asset)
    {
        var resource = bodyBuilder.LinkedResources.Add(
            asset.FileName,
            asset.Bytes,
            ContentType.Parse(asset.ContentType)
        );
        resource.ContentId = asset.ContentId;
        resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
    }

    private static void AddRecipients(MimeMessage mimeMessage, SentMessage message)
    {
        // Recipients marcados Suppressed (chequeo previo al send, Fase 7) nunca reciben el MIME real.
        foreach (var recipient in message.Recipients.Where(r => r.Status != RecipientStatus.Suppressed))
        {
            var mailbox = new MailboxAddress(recipient.DisplayName, recipient.Address);
            switch (recipient.Type)
            {
                case RecipientType.To:
                    mimeMessage.To.Add(mailbox);
                    break;
                case RecipientType.Cc:
                    mimeMessage.Cc.Add(mailbox);
                    break;
                case RecipientType.Bcc:
                    mimeMessage.Bcc.Add(mailbox);
                    break;
            }
        }
    }
}
