namespace TaxVision.Connectors.Application.Messages;

public sealed record MessageBodyAttachmentDto(string AttachmentId, string Filename, string ContentType, long SizeBytes);

public sealed record MessageBodyDto(
    long MimeSize,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<MessageBodyAttachmentDto> Attachments
);
