using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes;

public sealed record NoteAttachmentResponse(
    Guid Id,
    Guid CloudStorageFileId,
    string DisplayName,
    string ContentType,
    long SizeBytes,
    string Status,
    string? RejectionReason,
    DateTime LinkedAtUtc
)
{
    public static NoteAttachmentResponse From(NoteAttachment attachment) =>
        new(
            attachment.Id,
            attachment.CloudStorageFileId,
            attachment.DisplayName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Status.ToString(),
            attachment.RejectionReason,
            attachment.LinkedAtUtc
        );
}

public sealed record NoteResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string ContentHtml,
    string ContentPreview,
    string TargetType,
    Guid? TargetId,
    string Visibility,
    string? ColorKind,
    bool IsPinned,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<NoteAttachmentResponse> Attachments
)
{
    public static NoteResponse From(Note note) =>
        new(
            note.Id,
            note.TenantId,
            note.CreatedByUserId,
            note.Content.Html,
            note.Content.PlainTextPreview,
            note.Reference.TargetType.ToString(),
            note.Reference.TargetId,
            note.Visibility.ToString(),
            note.Color?.Kind.ToString(),
            note.IsPinned,
            note.Status.ToString(),
            note.CreatedAtUtc,
            note.UpdatedAtUtc,
            note.Attachments.Select(NoteAttachmentResponse.From).ToList()
        );
}
