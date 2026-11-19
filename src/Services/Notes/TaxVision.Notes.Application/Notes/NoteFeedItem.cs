using TaxVision.Notes.Domain.Notes;

namespace TaxVision.Notes.Application.Notes;

/// <summary>Ítem del feed (contrato §3): los campos de <see cref="NoteResponse"/> más <c>customerName</c>.</summary>
public sealed record NoteFeedItem(
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
    IReadOnlyList<NoteAttachmentResponse> Attachments,
    string? CustomerName
)
{
    public static NoteFeedItem From(Note note, string? customerName)
    {
        var r = NoteResponse.From(note);
        return new(
            r.Id,
            r.TenantId,
            r.CreatedByUserId,
            r.ContentHtml,
            r.ContentPreview,
            r.TargetType,
            r.TargetId,
            r.Visibility,
            r.ColorKind,
            r.IsPinned,
            r.Status,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            r.Attachments,
            customerName
        );
    }
}
