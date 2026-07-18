namespace TaxVision.Postmaster.Api.Requests;

/// <summary>Body de <c>POST /postmaster/correspondence-messages</c> (D3 Compose §14) — adjuntos por referencia, nunca bytes: Postmaster los trae de CloudStorage recién al enviar.</summary>
public sealed record SendCorrespondenceMessageRequest(
    Guid TenantId,
    Guid CorrespondenceDraftId,
    Guid AccountId,
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    IReadOnlyList<CorrespondenceAttachmentRequest>? Attachments,
    CorrespondenceReplyContextRequest? ReplyContext
);

public sealed record CorrespondenceAttachmentRequest(Guid FileId, string Filename, string ContentType, long SizeBytes);

/// <summary>Null si es correspondencia nueva (D3 Compose §13) — los 3 campos de threading viajan juntos o no viajan.</summary>
public sealed record CorrespondenceReplyContextRequest(
    string? InReplyToInternetMessageId,
    IReadOnlyList<string>? References,
    string? ReplyToProviderMessageId
);
