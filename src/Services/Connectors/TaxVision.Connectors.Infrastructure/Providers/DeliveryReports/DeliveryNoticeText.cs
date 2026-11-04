using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

/// <summary>
/// El texto legible del aviso, el que escribió el servidor para humanos. Nunca entra al correo original
/// adjunto, y corta donde el aviso empieza a copiarlo en línea ("Original message headers:", etc.).
/// </summary>
internal static partial class DeliveryNoticeText
{
    // Desde acá el aviso copia el correo original (o sus headers): nunca se lee ni se muestra.
    private static readonly string[] OriginalMarkers =
    [
        "original message headers",
        "----- original message -----",
        "-----original message-----",
        "this is a copy of the message",
        "below this line is a copy of the message",
        "original message follows",
        "unsent message below",
        "message header follows",
        "encabezados del mensaje original",
        "-----mensaje original-----",
        "----- mensaje original -----",
    ];

    // Desde acá es detalle técnico para administradores: sirve para leer códigos, no para el resumen.
    private static readonly string[] AdminMarkers =
    [
        "diagnostic information for administrators",
        "información de diagnóstico para administradores",
        "informacion de diagnostico para administradores",
    ];

    /// <summary>Primera parte text/plain del aviso; si no hay, la HTML pasada a texto.</summary>
    public static string Read(MimeMessage message)
    {
        TextPart? plain = null;
        TextPart? html = null;
        Collect(message.Body, ref plain, ref html);

        if (plain is not null)
            return Normalize(plain.Text);
        return html is null ? string.Empty : Normalize(HtmlToText(html.Text));
    }

    public static string WithoutOriginal(string text) => CutAt(text, OriginalMarkers);

    /// <summary>Resumen para humanos: sin la sección técnica y sin líneas vacías. Null si no queda nada.</summary>
    public static string? Summarize(string noticeText)
    {
        var lines = CutAt(noticeText, AdminMarkers)
            .Split('\n')
            .Select(line => WhitespaceRun().Replace(line, " ").Trim())
            .Where(line => line.Length > 0);
        var summary = string.Join('\n', lines);
        return summary.Length == 0 ? null : summary;
    }

    private static void Collect(MimeEntity? entity, ref TextPart? plain, ref TextPart? html)
    {
        switch (entity)
        {
            // El correo original adjunto y sus headers no son del aviso.
            case MessagePart:
                break;
            case Multipart multipart:
                foreach (var child in multipart)
                    Collect(child, ref plain, ref html);
                break;
            case TextPart text when !text.IsAttachment:
                if (text.IsPlain)
                    plain ??= text;
                else if (text.IsHtml)
                    html ??= text;
                break;
        }
    }

    private static string CutAt(string text, string[] markers)
    {
        var lines = text.Split('\n');
        var kept = new StringBuilder();
        foreach (var line in lines)
        {
            if (markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                break;
            kept.Append(line).Append('\n');
        }
        return kept.ToString().TrimEnd();
    }

    private static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;
        var text = HiddenBlocks().Replace(html, " ");
        text = LineBreakTags().Replace(text, "\n");
        text = AnyTag().Replace(text, " ");
        return WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"<(head|style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenBlocks();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|tr|li|h[1-6]|table|pre)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex WhitespaceRun();
}
