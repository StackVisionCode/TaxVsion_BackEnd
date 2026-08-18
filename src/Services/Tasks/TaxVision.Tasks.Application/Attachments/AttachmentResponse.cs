using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Attachments;

/// <param name="TaskId">
/// Con <c>includeDescendants</c> el listado mezcla los del padre y los de las subtareas; sin este
/// campo el frontend no sabría de cuál cuelga cada uno.
/// </param>
public sealed record TaskAttachmentResponse(
    Guid Id,
    Guid TaskId,
    Guid FileId,
    string DisplayName,
    string? ContentType,
    long SizeBytes,
    AttachmentOrigin Origin,
    AttachmentStatus Status,
    string? RejectionReason,
    Guid AttachedByUserId,
    DateTime AttachedAtUtc,
    DateTime? DetachedAtUtc
)
{
    public static TaskAttachmentResponse From(TaskAttachment attachment) =>
        new(
            attachment.Id,
            attachment.TaskId,
            attachment.FileId,
            attachment.DisplayName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.Origin,
            attachment.Status,
            attachment.RejectionReason,
            attachment.AttachedByUserId,
            attachment.AttachedAtUtc,
            attachment.DetachedAtUtc
        );
}
