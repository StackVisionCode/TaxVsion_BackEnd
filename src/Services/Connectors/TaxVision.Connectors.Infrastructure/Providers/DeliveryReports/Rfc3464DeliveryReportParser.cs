using MimeKit;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

/// <summary>
/// Lee un aviso de no entrega. Camino principal, RFC 3464: la parte <c>message/delivery-status</c> trae un
/// bloque por destinatario (Final-Recipient, Action, Status, Diagnostic-Code) y el correo original viene
/// adjunto como <c>message/rfc822</c> o <c>text/rfc822-headers</c>. Gmail, Exchange y Postfix lo usan.
/// Si no hay esa parte, prueba el texto del aviso (<see cref="PlainTextBounceReader"/>), que solo
/// responde cuando tiene destinatario y Message-ID original, sin adivinar.
/// </summary>
public sealed class Rfc3464DeliveryReportParser : IDeliveryReportParser
{
    public DeliveryReport Parse(byte[] rawMime)
    {
        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(rawMime, writable: false);
            message = MimeMessage.Load(stream);
        }
        catch (FormatException)
        {
            return DeliveryReport.Empty;
        }

        MessageDeliveryStatus? report = null;
        string? originalMessageId = null;
        FindReportParts(message.Body, ref report, ref originalMessageId);

        // Sin el correo adjunto, algunos servidores lo ponen como respuesta al original.
        originalMessageId ??= ReplyParentOf(message);

        var noticeText = DeliveryNoticeText.WithoutOriginal(DeliveryNoticeText.Read(message));
        var entries = report is not null
            ? ReadRecipients(report, originalMessageId)
            : PlainTextBounceReader.Read(message, noticeText, originalMessageId);

        return new DeliveryReport(
            entries,
            string.IsNullOrWhiteSpace(message.Subject) ? null : message.Subject.Trim(),
            message.From.Mailboxes.FirstOrDefault()?.Address,
            DeliveryNoticeText.Summarize(noticeText)
        );
    }

    /// <summary>Recorre el aviso sin entrar al correo original adjunto.</summary>
    private static void FindReportParts(
        MimeEntity? entity,
        ref MessageDeliveryStatus? report,
        ref string? originalMessageId
    )
    {
        switch (entity)
        {
            case MessageDeliveryStatus status:
                report ??= status;
                break;
            case MessagePart part:
                if (originalMessageId is null && !string.IsNullOrWhiteSpace(part.Message?.MessageId))
                    originalMessageId = part.Message.MessageId;
                break;
            case Multipart multipart:
                foreach (var child in multipart)
                    FindReportParts(child, ref report, ref originalMessageId);
                break;
        }
    }

    /// <summary>In-Reply-To, o el último de References (el padre directo).</summary>
    private static string? ReplyParentOf(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.InReplyTo))
            return message.InReplyTo;
        return message.References.Count > 0 ? message.References[^1] : null;
    }

    /// <summary>El primer grupo es del mensaje (Reporting-MTA…); los siguientes, uno por destinatario.</summary>
    private static List<DeliveryReportEntry> ReadRecipients(MessageDeliveryStatus report, string? originalMessageId)
    {
        var entries = new List<DeliveryReportEntry>();
        var groups = report.StatusGroups;
        for (var i = 1; i < groups.Count; i++)
        {
            var group = groups[i];
            var action = group["Action"]?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                continue;

            entries.Add(
                new DeliveryReportEntry(
                    originalMessageId,
                    AddressOf(group["Final-Recipient"] ?? group["Original-Recipient"]),
                    action,
                    group["Status"]?.Trim(),
                    AddressTypeStripped(group["Diagnostic-Code"])
                )
            );
        }
        return entries;
    }

    /// <summary>"rfc822; maria@cliente.com" → "maria@cliente.com".</summary>
    private static string? AddressOf(string? value)
    {
        var stripped = AddressTypeStripped(value);
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped.Trim('<', '>', ' ');
    }

    private static string? AddressTypeStripped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var semicolon = value.IndexOf(';');
        return (semicolon >= 0 ? value[(semicolon + 1)..] : value).Trim();
    }
}
