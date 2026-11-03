using System.Text.RegularExpressions;
using MimeKit;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Sync;

namespace TaxVision.Connectors.Infrastructure.Providers.DeliveryReports;

/// <summary>
/// Respaldo para avisos sin <c>message/delivery-status</c>: NDR de Exchange en texto ("Remote Server
/// returned '550 5.1.1 …'"), avisos de Gmail en texto plano y rebotes de Postfix/Exim
/// ("&lt;x@y&gt;: host … said: 550 5.1.1 …"). Conservador: solo responde si el aviso viene de un sistema
/// de correo, nombra al destinatario de forma explícita y se conoce el Message-ID original.
/// </summary>
internal static partial class PlainTextBounceReader
{
    // Encabezados de bloque tras los que vienen las direcciones que fallaron, una por línea.
    private static readonly string[] RecipientListHeaders =
    [
        "delivery has failed to these recipients",
        "delivery is delayed to these recipients",
        "delivery to the following recipient",
        "could not be delivered to the following recipient",
        "following address(es) failed",
        "following addresses had permanent fatal errors",
        "following addresses had transient non-fatal errors",
        "no se pudo entregar a estos destinatarios",
        "la entrega a estos destinatarios o grupos",
        "se ha retrasado la entrega a estos destinatarios",
    ];

    // Tras estas líneas viene la respuesta del servidor que rebotó.
    private static readonly string[] ResponseHeaders =
    [
        "the response from the remote server was",
        "the response was",
        "the error that the other server returned was",
        "smtp error from remote mail server",
    ];

    // Señales de que es un aviso de demora (todavía se reintenta), no un fallo.
    private static readonly string[] DelayMarkers =
    [
        "delivery is delayed",
        "delivery delayed",
        "has been delayed",
        "delivery incomplete",
        "temporary problem delivering",
        "will retry",
        "will be retried",
        "this is a warning only",
        "(delay)",
        "entrega retrasada",
        "se ha retrasado",
    ];

    public static IReadOnlyList<DeliveryReportEntry> Read(
        MimeMessage message,
        string noticeText,
        string? originalMessageId
    )
    {
        // Sin el correo original no hay nada que atar; sin pinta de aviso, no se lee el texto.
        if (string.IsNullOrWhiteSpace(originalMessageId) || !LooksLikeNotice(message))
            return [];

        var lines = noticeText.Split('\n');
        var found = new Dictionary<string, (string Address, string? Diagnostic)>(StringComparer.OrdinalIgnoreCase);
        void Add(string address, string? diagnostic)
        {
            if (found.TryGetValue(address, out var known) && known.Diagnostic is not null)
                return;
            found[address] = (address, Clean(diagnostic));
        }

        ReadPostfixLines(lines, Add);
        ReadExchangeLines(lines, Add);
        ReadDeliverySentences(noticeText, Add);
        ReadRecipientLists(lines, Add);
        foreach (var address in FailedRecipientsHeader(message))
            Add(address, null);

        var ownAddresses = message
            .From.Mailboxes.Concat(message.To.Mailboxes)
            .Concat(message.Cc.Mailboxes)
            .Select(m => m.Address)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generalDiagnostic = GeneralDiagnostic(lines);
        var delayed = DelayMarkers.Any(marker =>
            (message.Subject ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase)
            || noticeText.Contains(marker, StringComparison.OrdinalIgnoreCase)
        );

        var entries = new List<DeliveryReportEntry>();
        foreach (var (address, ownDiagnostic) in found.Values)
        {
            if (ownAddresses.Contains(address) || DeliveryReportCandidate.IsSystemSender(address))
                continue;

            var diagnostic = ownDiagnostic ?? generalDiagnostic;
            var status = StatusOf(ownDiagnostic) ?? StatusOf(generalDiagnostic);
            // Un 5.x.x es definitivo aunque el texto hable de demora.
            var action = delayed && status?.StartsWith('5') != true ? "delayed" : "failed";
            entries.Add(new DeliveryReportEntry(originalMessageId, address, action, status, diagnostic));
        }
        return entries;
    }

    /// <summary>Remitente de sistema, multipart/report o Auto-Submitted: un correo de persona no pasa.</summary>
    private static bool LooksLikeNotice(MimeMessage message)
    {
        if (DeliveryReportCandidate.IsSystemSender(message.From.Mailboxes.FirstOrDefault()?.Address))
            return true;
        if (message.Body?.ContentType.IsMimeType("multipart", "report") == true)
            return true;
        var autoSubmitted = message.Headers["Auto-Submitted"];
        return !string.IsNullOrWhiteSpace(autoSubmitted)
            && !autoSubmitted.Trim().Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Postfix/Exim: "&lt;x@y&gt;: host … said: 550 5.1.1 …" y sus líneas sangradas de continuación.</summary>
    private static void ReadPostfixLines(string[] lines, Action<string, string?> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var match = PostfixLine().Match(lines[i]);
            if (!match.Success)
                continue;

            var diagnostic = match.Groups["rest"].Value;
            for (var j = i + 1; j < lines.Length && lines[j].Length > 0 && char.IsWhiteSpace(lines[j][0]); j++)
                diagnostic += " " + lines[j];
            if (!string.IsNullOrWhiteSpace(diagnostic))
                add(match.Groups["addr"].Value, diagnostic);
        }
    }

    /// <summary>
    /// Exchange: "Remote Server returned '550 5.1.1 …'" debajo de la línea con la dirección, o el formato
    /// viejo "#550 5.1.1 … ##rfc822;x@y".
    /// </summary>
    private static void ReadExchangeLines(string[] lines, Action<string, string?> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var legacy = ExchangeLegacyLine().Match(lines[i]);
            if (legacy.Success)
            {
                add(legacy.Groups["addr"].Value, legacy.Groups["diag"].Value);
                continue;
            }

            var returned = RemoteServerReturned().Match(lines[i]);
            if (!returned.Success)
                continue;

            // La dirección está en la misma línea o en una de las tres anteriores con texto.
            var address = FirstAddress(lines[i][..returned.Index]);
            for (int j = i - 1, seen = 0; address is null && j >= 0 && seen < 3; j--)
            {
                if (string.IsNullOrWhiteSpace(lines[j]))
                    continue;
                seen++;
                address = FirstAddress(lines[j]);
            }
            if (address is not null)
                add(address, returned.Groups["diag"].Value);
        }
    }

    /// <summary>Frases de Gmail y Exchange que nombran al destinatario ("wasn't delivered to x@y").</summary>
    private static void ReadDeliverySentences(string text, Action<string, string?> add)
    {
        foreach (Match match in DeliverySentence().Matches(text))
            add(match.Groups["addr"].Value, null);
    }

    /// <summary>"Delivery has failed to these recipients or groups:" seguido de una dirección por línea.</summary>
    private static void ReadRecipientLists(string[] lines, Action<string, string?> add)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!RecipientListHeaders.Any(h => lines[i].Contains(h, StringComparison.OrdinalIgnoreCase)))
                continue;

            var listed = 0;
            for (int j = i + 1, textLines = 0; j < lines.Length && textLines < 6; j++)
            {
                var line = lines[j].Trim();
                if (line.Length == 0)
                    continue;
                textLines++;

                var address = AddressLine().IsMatch(line.TrimEnd('.')) ? FirstAddress(line) : null;
                if (address is null)
                {
                    // La lista termina en la primera línea que no es una dirección.
                    if (listed > 0)
                        break;
                    continue;
                }
                add(address, null);
                listed++;
            }
        }
    }

    /// <summary>Exim y Gmail ponen X-Failed-Recipients: es el MTA diciendo quién falló.</summary>
    private static IEnumerable<string> FailedRecipientsHeader(MimeMessage message)
    {
        foreach (
            var header in message.Headers.Where(h =>
                h.Field.Equals("X-Failed-Recipients", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            foreach (var value in header.Value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                var address = FirstAddress(value);
                if (address is not null)
                    yield return address;
            }
        }
    }

    /// <summary>La respuesta del servidor para todo el aviso: tras "The response was:" o la primera línea con código SMTP.</summary>
    private static string? GeneralDiagnostic(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (!ResponseHeaders.Any(h => lines[i].Contains(h, StringComparison.OrdinalIgnoreCase)))
                continue;
            for (var j = i + 1; j < lines.Length && j <= i + 4; j++)
            {
                if (SmtpReply().IsMatch(lines[j]))
                    return Clean(lines[j]);
            }
        }

        var first = lines.FirstOrDefault(line => SmtpReply().IsMatch(line));
        return Clean(first);
    }

    private static string? StatusOf(string? diagnostic)
    {
        if (string.IsNullOrEmpty(diagnostic))
            return null;
        var match = EnhancedStatus().Match(diagnostic);
        return match.Success ? match.Value : null;
    }

    private static string? FirstAddress(string text)
    {
        var match = Address().Match(text);
        return match.Success ? match.Value.TrimEnd('.') : null;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Whitespace().Replace(value, " ").Trim().Trim('\'', '"');
    }

    private const string Email = @"[A-Za-z0-9._%+'=\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}";

    [GeneratedRegex(Email)]
    private static partial Regex Address();

    // La línea es solo una dirección: "x@y", "<x@y>", "Nombre (x@y)", "x@y (x@y)<mailto:x@y>".
    [GeneratedRegex(
        @"^(?:""?[^""@<>()]{1,60}""?\s*[(<]"
            + Email
            + @"[)>]|[<(]?"
            + Email
            + @"[>)]?)(?:\s*\("
            + Email
            + @"\))?(?:\s*<mailto:[^>\s]+>)?\s*:?$"
    )]
    private static partial Regex AddressLine();

    [GeneratedRegex(@"^<(?<addr>[^<>\s]+@[^<>\s]+)>:?\s*(?<rest>.*)$")]
    private static partial Regex PostfixLine();

    [GeneratedRegex(@"remote server returned\s*['""](?<diag>.+)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteServerReturned();

    [GeneratedRegex(@"#(?<diag>[^#]+?)\s*##rfc822;\s*<?(?<addr>[^\s<>#]+@[^\s<>#]+?)>?(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ExchangeLegacyLine();

    // Gmail: "wasn't delivered to x@y", "temporary problem delivering your message to x@y".
    // Exchange: "Your message to x@y couldn't be delivered." / "Su mensaje para x@y no se pudo entregar."
    [GeneratedRegex(
        @"(?:(?:wasn['’]t|was not) delivered to|temporary problem delivering your message to)\s+<?(?<addr>"
            + Email
            + @")|(?:your message to|su mensaje para|tu mensaje para)\s+<?(?<addr>"
            + Email
            + @")>?\s+(?:couldn['’]t be delivered|could not be delivered|no se pudo entregar|no se puede entregar)",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DeliverySentence();

    // Respuesta SMTP con código ampliado: "550 5.1.1 …", "550-5.1.1 …", "552 5.2.2 …".
    [GeneratedRegex(@"\b[245]\d\d[ \-]#?[245]\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex SmtpReply();

    // "5.1.1" sin agarrar pedazos de una IP (10.4.5.6) ni de una versión.
    [GeneratedRegex(@"(?<![\d.])[245]\.\d{1,3}\.\d{1,3}(?!\.?\d)")]
    private static partial Regex EnhancedStatus();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
