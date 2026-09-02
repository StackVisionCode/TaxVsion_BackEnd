using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>Metadata de un mensaje (formato metadata-only del proveedor) — nunca incluye el body ni bytes de attachments.</summary>
/// <summary>
/// <see cref="PartId"/> es el id de la parte MIME (Gmail: "1", "1.2") — estable entre fetches, a
/// diferencia de <see cref="ProviderAttachmentId"/> que Gmail rota. Es el selector preferido para
/// re-ubicar el adjunto al descargarlo. Null en proveedores que no lo exponen (IMAP/Graph).
/// </summary>
public sealed record RawMessageAttachment(
    string ProviderAttachmentId,
    string Filename,
    string ContentType,
    long SizeBytes,
    string? PartId = null
);

public sealed record RawMessage(
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string? InReplyTo,
    IReadOnlyList<string> References,
    string From,
    string? FromDisplayName,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string Snippet,
    DateTime ReceivedAtUtc,
    IReadOnlyList<RawMessageAttachment> Attachments,
    AuthenticationSignals AuthenticationSignals
)
{
    public bool HasAttachments => Attachments.Count > 0;
}
