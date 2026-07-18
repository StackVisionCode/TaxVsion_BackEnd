namespace TaxVision.Connectors.Api.Requests;

/// <summary>Body de <c>POST /connectors/accounts/{accountId}/send</c> (D3 §3.7) — espejo plano de <c>OutboundMessage</c>.</summary>
public sealed record SendMessageRequest(
    Guid TenantId,
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? ReplyToDisplayAddress,
    string? InReplyToInternetMessageId,
    IReadOnlyList<string>? References,
    string? ReplyToProviderMessageId,
    IReadOnlyList<SendMessageAttachmentRequest>? Attachments = null
);

/// <summary>Contenido en base64 (D3 Compose §16 Fase 1) — consistente con el resto de payloads binarios M2M del repo (ej. <c>EmailLayoutsController.PreviewPngBase64</c>).</summary>
public sealed record SendMessageAttachmentRequest(string Filename, string ContentType, string ContentBase64);
