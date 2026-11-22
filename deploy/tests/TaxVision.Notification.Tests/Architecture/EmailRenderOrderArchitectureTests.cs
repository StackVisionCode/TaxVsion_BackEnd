using System.Text.RegularExpressions;

namespace TaxVision.Notification.Tests.Architecture;

/// <summary>
/// El render de Scribe (HTTP de hasta 10 s) va antes de la primera escritura del handler: una transacción que aún
/// no escribió no bloquea filas. Comprobación de texto, método a método, en los archivos que usan Scribe.
/// </summary>
public sealed partial class EmailRenderOrderArchitectureTests
{
    private static readonly string[] RenderCalls =
    [
        ".RenderAsync(",
        ".RenderForRecipientAsync(",
        ".RenderTextAsync(",
        // RecipientRenders y los ayudantes que renderizan todo antes de encolar.
        ".ForAsync(",
        "OfficeAlertEmails.SendAsync(",
    ];

    private static readonly string[] WriteCalls =
    [
        ".RecordInAppAsync(",
        ".SendPushAsync(",
        ".DispatchPushAsync(",
        ".QueueEmailAsync(",
        ".SendSmsAsync(",
        ".DispatchAsync(",
        ".SaveChangesAsync(",
        ".AddAsync(",
        ".PublishAsync(",
    ];

    // Ramas excluyentes: la escritura que va antes en el texto está en una rama que termina en return sin renderizar.
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["MfaChallengeRequestedConsumer.Handle"] = "la rama SMS termina en return; el correo va en la otra rama",
        ["SignatureRequestReminderDueConsumer.Handle"] = "la rama SMS termina en return; el correo va en la otra",
        ["SignedDocumentsDeliveryReadyConsumer.Handle"] = "la rama SMS termina en return; el correo va en la otra",
        ["UserSlotUnlockedEmailConsumer.Handle"] = "los SaveChanges de las ramas sin correo terminan en return",
    };

    [Fact]
    public void Email_render_precedes_the_first_write()
    {
        var offenders = NotificationDispatchArchitectureTests
            .ApplicationFiles()
            .Select(File.ReadAllText)
            .Where(text => text.Contains("IScribeRenderClient", StringComparison.Ordinal))
            .SelectMany(Offenders)
            .Where(name => !Allowed.ContainsKey(name))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Escritura antes del render de Scribe (renderiza primero):\n" + string.Join("\n", offenders)
        );
    }

    private static IEnumerable<string> Offenders(string text)
    {
        foreach (Match method in AsyncMethod().Matches(text))
        {
            var body = Body(text, method.Index + method.Length - 1);
            var render = FirstIndex(body, RenderCalls);
            var write = FirstIndex(body, WriteCalls);
            if (render >= 0 && write >= 0 && write < render)
                yield return $"{ClassAt(text, method.Index)}.{method.Groups["name"].Value}";
        }
    }

    // Cuerpo del método a partir del paréntesis de sus parámetros.
    private static string Body(string text, int openParen)
    {
        var close = Matching(text, openParen, '(', ')');
        var brace = text.IndexOf('{', close);
        var arrow = text.IndexOf("=>", close, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
            return text[arrow..text.IndexOf(';', arrow)];
        return text[brace..(Matching(text, brace, '{', '}') + 1)];
    }

    private static int Matching(string text, int start, char open, char close)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == open)
                depth++;
            else if (text[i] == close && --depth == 0)
                return i;
        }
        return text.Length - 1;
    }

    private static int FirstIndex(string body, string[] calls) =>
        calls
            .Select(call => body.IndexOf(call, StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();

    private static string ClassAt(string text, int index) =>
        ClassName().Matches(text[..index]).LastOrDefault()?.Groups["name"].Value ?? "?";

    [GeneratedRegex(@"\basync\s+Task[^\n(]*\s(?<name>\w+)\s*\(")]
    private static partial Regex AsyncMethod();

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)")]
    private static partial Regex ClassName();
}
