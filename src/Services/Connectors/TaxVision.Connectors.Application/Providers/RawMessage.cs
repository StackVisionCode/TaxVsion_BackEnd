using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>Metadata de un mensaje (formato metadata-only del proveedor) — nunca incluye el body ni bytes de attachments.</summary>
public sealed record RawMessageAttachment(
    string ProviderAttachmentId,
    string Filename,
    string ContentType,
    long SizeBytes
);

public sealed record RawMessage(
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string? InReplyTo,
    IReadOnlyList<string> References,
    string From,
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
