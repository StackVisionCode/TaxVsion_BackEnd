using MimeKit;
using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

/// <summary>
/// Lee un aviso de no entrega estándar (RFC 3464): la parte <c>message/delivery-status</c> trae un bloque
/// por destinatario (Final-Recipient, Action, Status, Diagnostic-Code) y el correo original viene
/// adjunto como <c>message/rfc822</c> o <c>text/rfc822-headers</c>. Gmail, Exchange y Postfix lo usan.
/// </summary>
public sealed class Rfc3464DeliveryReportParser : IDeliveryReportParser
{
    public IReadOnlyList<DeliveryReportEntry> Parse(byte[] rawMime)
    {
        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(rawMime, writable: false);
            message = MimeMessage.Load(stream);
        }
        catch (FormatException)
        {
            return [];
        }

        MessageDeliveryStatus? report = null;
        string? originalMessageId = null;
        foreach (var part in message.BodyParts)
        {
            if (part is MessageDeliveryStatus status)
                report ??= status;
            else if (part is MessagePart { Message: { } original } && originalMessageId is null)
                originalMessageId = original.MessageId;
        }

        if (report is null)
            return [];

        // Sin el correo adjunto, algunos servidores lo ponen como respuesta al original.
        originalMessageId ??= message.InReplyTo;
        return ReadRecipients(report, originalMessageId);
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
