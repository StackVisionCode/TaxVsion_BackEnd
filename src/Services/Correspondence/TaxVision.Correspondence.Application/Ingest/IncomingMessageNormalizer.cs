using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Valores del mensaje entrante ya ajustados a los límites del dominio.</summary>
public sealed record NormalizedIncomingMessage(
    string Subject,
    string Snippet,
    string? InternetMessageId,
    string? InReplyTo,
    IReadOnlyList<string>? References,
    string? ProviderThreadId
);

/// <summary>
/// Ajusta en la frontera lo que el dominio rechazaría por forma (asunto vacío o largo, cabeceras largas) para
/// que un correo válido de un cliente no se pierda. Un identificador demasiado largo se anula en vez de
/// recortarse: recortado ya no sirve para emparejar.
/// </summary>
public static class IncomingMessageNormalizer
{
    public static NormalizedIncomingMessage Normalize(ConnectorsRawMessageReceivedIntegrationEvent evt) =>
        new(
            NormalizeSubject(evt.Subject),
            Truncate(evt.Snippet ?? "", IncomingEmail.SnippetMaxLength),
            NullIfLonger(evt.InternetMessageId, IncomingEmail.InternetMessageIdMaxLength),
            NullIfLonger(evt.InReplyTo, IncomingEmail.InReplyToMaxLength),
            KeepMostRecentReferences(evt.References),
            NullIfLonger(evt.ProviderThreadId, EmailThread.ProviderThreadIdMaxLength)
        );

    private static string NormalizeSubject(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? EmailThread.NoSubject : Truncate(subject, EmailThread.SubjectMaxLength);

    // Las últimas referencias son las más recientes: se conservan mientras quepan unidas por comas.
    private static IReadOnlyList<string>? KeepMostRecentReferences(IReadOnlyList<string>? references)
    {
        if (references is not { Count: > 0 })
            return null;

        var kept = new List<string>();
        var length = 0;
        for (var i = references.Count - 1; i >= 0; i--)
        {
            var next = length + references[i].Length + (kept.Count > 0 ? 1 : 0);
            if (next > IncomingEmail.ReferencesMaxLength)
                break;
            kept.Add(references[i]);
            length = next;
        }

        if (kept.Count == 0)
            return null;
        kept.Reverse();
        return kept;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private static string? NullIfLonger(string? value, int maxLength) => value?.Length > maxLength ? null : value;
}
