using TaxVision.Tasks.ClientForms.Application.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record InitiateExternalFormAttachmentUploadCommand(
    ParticipantContext Context,
    string ComponentId,
    string? RowId,
    string OriginalName,
    string ContentType,
    long SizeBytes,
    DateTime NowUtc
);

public sealed record CompleteExternalFormAttachmentUploadCommand(ParticipantContext Context, Guid FileId);

public sealed record ExternalFormAttachmentUploadResponse(
    Guid FileId,
    Guid AttachmentId,
    ExternalFormPresignedPost PresignedPost,
    string RowVersion
);

public sealed record ExternalFormPresignedPost(
    Uri Url,
    IReadOnlyDictionary<string, string> FormData,
    DateTime ExpiresAtUtc
);
